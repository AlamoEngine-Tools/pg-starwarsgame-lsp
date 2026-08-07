// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Dat;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Creates the sibling file that adds a language to a single-language localisation project.
/// </summary>
/// <remarks>
///     The new file is seeded with the source file's keys and empty values, in the source's order, so
///     it opens as a translation worklist rather than as a blank page. Nothing is copied across as a
///     value: a key left holding the other language's text is indistinguishable from a finished
///     translation, and the coverage inspector could not tell you what still needs doing.
/// </remarks>
public sealed class CreateLocalisationLanguageFileHandler
    : IJsonRpcRequestHandler<CreateLocalisationLanguageFileParams, CreateLocalisationLanguageFileResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly ILocalisationFormatConverter _converter;
    private readonly IDatFileService _datFileService;
    private readonly IDatTranslationExporter _datExporter;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IFileHelper _fileHelper;
    private readonly ILanguageService _langService;
    private readonly ILogger<CreateLocalisationLanguageFileHandler> _logger;
    private readonly ILocalisationProjectRegistry _projectRegistry;
    private readonly IModProjectReloadService _reloadService;
    private readonly ILocalisationRowReader _rowReader;

    public CreateLocalisationLanguageFileHandler(
        ILocalisationRowReader rowReader,
        ILocalisationFormatConverter converter,
        ITranslationDatabaseFactory factory,
        IDatTranslationExporter datExporter,
        IDatFileService datFileService,
        ILanguageService langService,
        IFileHelper fileHelper,
        ILocalisationProjectRegistry projectRegistry,
        IModProjectReloadService reloadService,
        ILogger<CreateLocalisationLanguageFileHandler> logger,
        ILspConfigurationProvider config)
    {
        _rowReader = rowReader;
        _converter = converter;
        _factory = factory;
        _datExporter = datExporter;
        _datFileService = datFileService;
        _langService = langService;
        _fileHelper = fileHelper;
        _projectRegistry = projectRegistry;
        _reloadService = reloadService;
        _logger = logger;
        _config = config;
    }

    public async Task<CreateLocalisationLanguageFileResult> Handle(
        CreateLocalisationLanguageFileParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Tools.Localisation)
            return Fail(LocalisationFeatureDisabled.Message);

        if (string.IsNullOrWhiteSpace(request.ProjectFilePath))
            return Fail("No localisation file was given.");

        var fs = _fileHelper.FileSystem;
        if (!fs.File.Exists(request.ProjectFilePath))
            return Fail($"'{request.ProjectFilePath}' does not exist.");

        if (!_langService.TryGetByIdentifier(request.Language ?? string.Empty, out var language)
            || language is null)
            return Fail($"'{request.Language}' is not a language this game supports.");

        var extension = fs.Path.GetExtension(request.ProjectFilePath).ToLowerInvariant();
        if (!LocalisationFileNameLanguageResolver.CarriesLanguageInFileName(extension))
            return Fail(
                $"{extension} files hold every language in one file - add a column instead of a file.");

        var targetPath = SiblingPath(request.ProjectFilePath, extension, language.LanguageIdentifier);
        if (fs.File.Exists(targetPath))
            return Fail(
                $"'{fs.Path.GetFileName(targetPath)}' already exists - that language is already part " +
                "of this project.");

        LocDocument source;
        try
        {
            source = _rowReader.ReadFile(request.ProjectFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "aet/createLocalisationLanguageFile: could not read '{Path}'.",
                request.ProjectFilePath);
            return Fail($"Could not read '{request.ProjectFilePath}': {ex.Message}");
        }

        // Credits repeat their keys and their order is the content, so a keyed database would
        // collapse the crawl into whichever line happened to come last.
        var isCredits = LocalisationCategoryResolver.Resolve(
            _projectRegistry, _fileHelper, request.ProjectFilePath) == LocCategory.Credits;
        ITranslationDatabase db = isCredits
            ? _factory.CreateOrdered([language])
            : _factory.CreateKeyed([language]);

        foreach (var row in source.Rows)
            db.SetTranslation(row.Key, language, string.Empty);

        if (!await WriteAsync(db, extension, targetPath, language, ct))
            return Fail($"Could not write '{targetPath}'.");

        _logger.LogInformation(
            "aet/createLocalisationLanguageFile: created '{Target}' with {Count} key(s) from '{Source}'.",
            targetPath, source.Rows.Count, request.ProjectFilePath);

        // The tree and the index only learn about a new file through a reload; the watcher would get
        // there eventually, but the user is looking at the result now.
        await _reloadService.ReloadLocalisationAsync(ct);
        return new CreateLocalisationLanguageFileResult(targetPath);
    }

    /// <summary>
    ///     The source's name with its language suffix replaced by the new one, lowercased to match the
    ///     engine's own files.
    /// </summary>
    private string SiblingPath(string sourcePath, string extension, string languageIdentifier)
    {
        var fs = _fileHelper.FileSystem;
        var stem = fs.Path.GetFileNameWithoutExtension(sourcePath);

        // A source that already names a language keeps everything before it; one that does not keeps
        // its whole name, so mastertextfile.properties yields mastertextfile_german.properties.
        if (LocalisationFileNameLanguageResolver.TryResolve(sourcePath, _langService, out _))
        {
            var underscoreIdx = stem.LastIndexOf('_');
            if (underscoreIdx >= 0) stem = stem[..underscoreIdx];
        }

        // Built by replacing the file name in place so the caller's separators survive - see
        // ConvertLocalisationFormatHandler.TargetsPerLanguage.
        var nameStart = sourcePath.Length - fs.Path.GetFileName(sourcePath).Length;
        return $"{sourcePath[..nameStart]}{stem.ToLowerInvariant()}_" +
               $"{languageIdentifier.ToLowerInvariant()}{extension}";
    }

    private async Task<bool> WriteAsync(
        ITranslationDatabase db, string extension, string targetPath,
        PG.StarWarsGame.Localisation.Languages.IAlamoLanguageDefinition language, CancellationToken ct)
    {
        if (extension != ".dat")
            return await _converter.WriteAsync(db, "nls", targetPath, ct);

        // DAT is binary, so it goes out through the DAT services rather than the text converter.
        try
        {
            var model = _datExporter.Export(db, language);
            var fs = _fileHelper.FileSystem;
            using var stream = fs.File.Create(targetPath);
            _datFileService.CreateDatFile(stream, model, model.KeySortOrder);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "aet/createLocalisationLanguageFile: could not write '{Path}'.",
                targetPath);
            return false;
        }
    }

    private CreateLocalisationLanguageFileResult Fail(string message)
    {
        _logger.LogWarning("aet/createLocalisationLanguageFile: {Reason}", message);
        return new CreateLocalisationLanguageFileResult(null, message);
    }
}
