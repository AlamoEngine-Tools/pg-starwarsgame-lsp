// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Dat;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Rewrites one localisation file in another text format and repoints the project at it.
/// </summary>
/// <remarks>
///     The original file is kept, by the user's explicit choice: a conversion is not reversible
///     through undo (it writes to disk, like every other localisation write), so the previous
///     encoding stays on disk as the fallback. The loader picks files by the project's declared
///     format, so the leftover is inert rather than ambiguous - but it is still listed, which is why
///     the result says how many files were left in the old format.
/// </remarks>
public sealed class ConvertLocalisationFormatHandler
    : IJsonRpcRequestHandler<ConvertLocalisationFormatParams, ConvertLocalisationFormatResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly ILocalisationFormatConverter _converter;
    private readonly ICsvTranslationImporter _csvImporter;
    private readonly IDatFileService _datFileService;
    private readonly IDatTranslationImporter _datImporter;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IModProjectFileWriter _fileWriter;
    private readonly IFileHelper _fileHelper;
    private readonly ILanguageService _langService;
    private readonly ILogger<ConvertLocalisationFormatHandler> _logger;
    private readonly IPropertiesTranslationImporter _nlsImporter;
    private readonly ILocalisationProjectRegistry _projectRegistry;
    private readonly IModProjectReloadService _reloadService;
    private readonly IXmlTranslationImporter _xmlImporter;

    public ConvertLocalisationFormatHandler(
        ICsvTranslationImporter csvImporter,
        IXmlTranslationImporter xmlImporter,
        IPropertiesTranslationImporter nlsImporter,
        IDatTranslationImporter datImporter,
        IDatFileService datFileService,
        ITranslationDatabaseFactory factory,
        ILanguageService langService,
        ILocalisationFormatConverter converter,
        IFileHelper fileHelper,
        ILocalisationProjectRegistry projectRegistry,
        IModProjectReloadService reloadService,
        IModProjectFileWriter fileWriter,
        ILogger<ConvertLocalisationFormatHandler> logger,
        ILspConfigurationProvider config)
    {
        _csvImporter = csvImporter;
        _xmlImporter = xmlImporter;
        _nlsImporter = nlsImporter;
        _datImporter = datImporter;
        _datFileService = datFileService;
        _factory = factory;
        _langService = langService;
        _converter = converter;
        _fileHelper = fileHelper;
        _projectRegistry = projectRegistry;
        _reloadService = reloadService;
        _fileWriter = fileWriter;
        _logger = logger;
        _config = config;
    }

    public async Task<ConvertLocalisationFormatResult> Handle(
        ConvertLocalisationFormatParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Tools.Localisation)
            return Fail(LocalisationFeatureDisabled.Message);

        if (string.IsNullOrWhiteSpace(request.ProjectFilePath))
            return Fail("No localisation file was given to convert.");

        var fs = _fileHelper.FileSystem;
        if (!fs.File.Exists(request.ProjectFilePath))
            return Fail($"Cannot convert: '{request.ProjectFilePath}' does not exist.");

        var targetFormat = request.TargetFormat ?? string.Empty;
        if (string.Equals(targetFormat, "dat", StringComparison.OrdinalIgnoreCase))
            return Fail(
                "DAT is not a conversion target. It is the format the game loads, one binary file " +
                "per language - use 'Export Localisation to DAT' to produce it.");

        var targetExtension = _converter.ExtensionFor(targetFormat);
        if (targetExtension is null)
            return Fail($"'{request.TargetFormat}' is not a format that can be written. Use CSV, XML or NLS.");

        var sourceExtension = fs.Path.GetExtension(request.ProjectFilePath).ToLowerInvariant();
        if (string.Equals(sourceExtension, targetExtension, StringComparison.OrdinalIgnoreCase))
            return Fail($"'{request.ProjectFilePath}' is already in that format.");

        var targetPath = fs.Path.ChangeExtension(request.ProjectFilePath, targetExtension);
        if (fs.File.Exists(targetPath))
            return Fail(
                $"Cannot convert: '{targetPath}' already exists and was left untouched. Delete or " +
                "move it first.");

        // Credits repeat their keys and their order is the content, so they need the ordered
        // database - a keyed one silently collapses the crawl. Nothing is seeded underneath either
        // kind: a conversion re-encodes this one file, it does not merge the game's text into it.
        var languages = _langService.OfficiallySupported();
        var isCredits = LocalisationCategoryResolver.Resolve(
            _projectRegistry, _fileHelper, request.ProjectFilePath) == LocCategory.Credits;
        ITranslationDatabase db = isCredits
            ? _factory.CreateOrdered(languages)
            : _factory.CreateKeyed(languages);

        if (await ReadSourceAsync(request.ProjectFilePath, sourceExtension, db, ct) is { } readError)
            return Fail(readError);

        if (!await _converter.WriteAsync(db, targetFormat, targetPath, ct))
            return Fail($"'{request.TargetFormat}' is not a format that can be written. Use CSV, XML or NLS.");

        _logger.LogInformation(
            "aet/convertLocalisationFormat: wrote '{Target}' from '{Source}'.",
            targetPath, request.ProjectFilePath);

        var (changed, leftBehind) = await RepointProjectAsync(sourceExtension, targetFormat, ct);
        return new ConvertLocalisationFormatResult(targetPath, changed, leftBehind);
    }

    /// <summary>Reads the file into <paramref name="db" />, or returns why it could not be.</summary>
    private async Task<string?> ReadSourceAsync(
        string path, string extension, ITranslationDatabase db, CancellationToken ct)
    {
        try
        {
            // DAT is binary and single-language, with the language carried by the file name rather
            // than by anything inside it.
            if (extension == ".dat")
            {
                if (!DatFileNameLanguageResolver.TryResolve(path, _langService, out var language))
                    return $"Cannot convert '{path}': its name does not say which language it holds " +
                           "(expected '..._<LANGUAGE>.dat').";

                using var datFile = _datFileService.Load(path);
                _datImporter.Import(datFile.Content, language!, db);
                return null;
            }

            var content = await _fileHelper.FileSystem.File.ReadAllTextAsync(path, ct);
            switch (extension)
            {
                case ".csv":
                    using (var reader = new StringReader(content))
                    {
                        _csvImporter.Import(reader, db);
                    }

                    return null;
                case ".xml":
                    _xmlImporter.Import(XDocument.Parse(content), db);
                    return null;
                case ".properties":
                    using (var reader = new StringReader(content))
                    {
                        _nlsImporter.Import(reader, _langService.Default, db);
                    }

                    return null;
                default:
                    return $"Cannot convert '{path}': {extension} is not a localisation format this understands.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "aet/convertLocalisationFormat: failed to read '{Path}'.", path);
            return $"Cannot convert '{path}': it could not be read - {ex.Message}";
        }
    }

    /// <summary>
    ///     Points the <c>.pgproj</c> at the new format, when the converted file is the one its
    ///     declared format describes.
    /// </summary>
    /// <remarks>
    ///     Converting a file that is *not* in the project's format - a stray .properties in a CSV
    ///     project - must not drag the whole project across with it. In that case the new file is
    ///     written and the project is left exactly as it was.
    /// </remarks>
    private async Task<(bool Changed, int LeftBehind)> RepointProjectAsync(
        string sourceExtension, string targetFormat, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var rootLayer = _reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank).FirstOrDefault();

        if (rootLayer?.ProjectPath is not { } pgprojPath ||
            rootLayer.TextResourceType is not { } declaredFormat ||
            rootLayer.TextRoots.Count == 0)
            return (false, 0);

        // Through the format utility, not the converter: the converter answers "can this be
        // written", which is false for DAT - so a DAT project never recognised its own files and
        // its .pgproj was left pointing at the old format.
        if (!string.Equals(LocalisationFormatUtility.ToExtension(declaredFormat), sourceExtension,
                StringComparison.OrdinalIgnoreCase))
            return (false, 0);

        var relative = fs.Path.GetRelativePath(fs.Path.GetDirectoryName(pgprojPath)!, rootLayer.TextRoots[0]);
        if (fs.Path.IsPathRooted(relative)) return (false, 0);

        await _fileWriter.SetLocalisationAsync(
            pgprojPath, targetFormat.ToUpperInvariant(), relative.Replace('\\', '/').ToLowerInvariant(), ct);
        await _reloadService.ReloadAsync(ct);

        // Everything still in the old format is now inert: the loader reads the declared format
        // only. The count is what lets the caller warn while the change is still fresh.
        var leftBehind = _projectRegistry.Projects.Count(p =>
            string.Equals(LocalisationFormatUtility.ToExtension(p.ResourceType), sourceExtension,
                StringComparison.OrdinalIgnoreCase)) - 1;

        return (true, Math.Max(0, leftBehind));
    }

    private ConvertLocalisationFormatResult Fail(string message)
    {
        _logger.LogWarning("aet/convertLocalisationFormat: {Reason}", message);
        return new ConvertLocalisationFormatResult(null, Error: message);
    }
}
