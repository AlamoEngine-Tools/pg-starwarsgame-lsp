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
using PG.StarWarsGame.Localisation.Languages;
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
        if (LocalisationFeatureDisabled.Rejection(_config) is { } rejection)
            return Fail(rejection);

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

        // A single-language target gets one file per language in the source. Writing one file would
        // mean picking a language and dropping the others, which is exactly the silent data loss this
        // avoids - and the name has to say which language each file holds anyway.
        var fansOutPerLanguage =
            LocalisationFileNameLanguageResolver.CarriesLanguageInFileName(targetExtension);

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

        var targets = fansOutPerLanguage
            ? TargetsPerLanguage(request.ProjectFilePath, targetExtension, db)
            : [fs.Path.ChangeExtension(request.ProjectFilePath, targetExtension)];

        if (targets.Count == 0)
            return Fail($"Cannot convert: '{request.ProjectFilePath}' holds no translations.");

        // Checked before anything is written, so a collision cannot leave half a fan-out on disk.
        foreach (var existing in targets.Where(fs.File.Exists))
            return Fail(
                $"Cannot convert: '{existing}' already exists and was left untouched. Delete or " +
                "move it first.");

        foreach (var target in targets)
            if (!await _converter.WriteAsync(db, targetFormat, target, ct))
                return Fail(
                    $"'{request.TargetFormat}' is not a format that can be written. Use CSV, XML or NLS.");

        _logger.LogInformation(
            "aet/convertLocalisationFormat: wrote {Count} file(s) from '{Source}': {Targets}.",
            targets.Count, request.ProjectFilePath, string.Join(", ", targets));

        var (changed, leftBehind) = await RepointProjectAsync(sourceExtension, targetFormat, ct);
        return new ConvertLocalisationFormatResult(targets, changed, leftBehind);
    }

    /// <summary>
    ///     One target path per language that actually carries a translation, named for that language.
    ///     <para>
    ///         Languages are taken from the entries rather than from <c>db.Languages</c>: the database
    ///         is created with every officially supported language registered, so the registered set
    ///         would produce a file per language the game knows, nearly all of them empty.
    ///     </para>
    /// </summary>
    private IReadOnlyList<string> TargetsPerLanguage(
        string sourcePath, string targetExtension, ITranslationDatabase db)
    {
        var fs = _fileHelper.FileSystem;
        var stem = fs.Path.GetFileNameWithoutExtension(sourcePath).ToLowerInvariant();

        // Built by replacing the source's file name in place rather than through Path.Combine, so the
        // separators the caller used survive - Combine would emit '\' on Windows for a path given
        // with '/', which the single-file branch (Path.ChangeExtension) does not do.
        var nameStart = sourcePath.Length - fs.Path.GetFileName(sourcePath).Length;
        var directoryPrefix = sourcePath[..nameStart];

        return db
            .SelectMany(entry => entry.Translations
                .Where(t => !string.IsNullOrEmpty(t.Value))
                .Select(t => t.Key.LanguageIdentifier))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => $"{directoryPrefix}{stem}_{id.ToLowerInvariant()}{targetExtension}")
            .ToList();
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
                if (!LocalisationFileNameLanguageResolver.TryResolve(path, _langService, out var language))
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
                        _nlsImporter.Import(reader, NlsLanguageOf(path), db);
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
        return new ConvertLocalisationFormatResult([], Error: message);
    }

    /// <summary>
    ///     The language a <c>.properties</c> source holds. NLS names its language in the file name and
    ///     nowhere else, so a file that does not name one falls back to the workspace's configured game
    ///     language rather than being assumed to be the service default.
    /// </summary>
    private IAlamoLanguageDefinition NlsLanguageOf(string path)
    {
        return LocalisationFileNameLanguageResolver.Resolve(
            path, _langService,
            LocalisationFileNameLanguageResolver.Configured(_langService, _config.Current.Localisation),
            out _);
    }
}
