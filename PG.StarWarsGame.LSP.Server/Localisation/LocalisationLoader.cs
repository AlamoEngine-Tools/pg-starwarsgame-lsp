// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.Data.Config.v2;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class LocalisationLoader : ILocalisationLoader
{
    private readonly IBaselineTranslationProvider _baselineProvider;
    private readonly ILspConfigurationProvider _configProvider;
    private readonly ICsvTranslationImporter _csvImporter;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILanguageService _langService;
    private readonly ILogger<LocalisationLoader> _logger;
    private readonly IPropertiesTranslationImporter _nlsImporter;
    private readonly IXmlTranslationImporter _xmlImporter;

    public LocalisationLoader(
        IBaselineTranslationProvider baselineProvider,
        ITranslationDatabaseFactory factory,
        ICsvTranslationImporter csvImporter,
        IXmlTranslationImporter xmlImporter,
        IPropertiesTranslationImporter nlsImporter,
        ILanguageService langService,
        ILspConfigurationProvider configProvider,
        IFileHelper fileHelper,
        IGameIndexService indexService,
        ILogger<LocalisationLoader> logger)
    {
        _baselineProvider = baselineProvider;
        _factory = factory;
        _csvImporter = csvImporter;
        _xmlImporter = xmlImporter;
        _nlsImporter = nlsImporter;
        _langService = langService;
        _configProvider = configProvider;
        _fileHelper = fileHelper;
        _indexService = indexService;
        _logger = logger;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        var config = _configProvider.Current;
        var locConfig = config.Localisation;

        if (!_langService.TryGetByIdentifier(config.Locale, out var language))
            language = _langService.Default;

        var eawDb = _baselineProvider.GetMasterText(GameType.EaW, language!);
        var focDb = _baselineProvider.GetMasterText(GameType.FoC, language!);

        var sourcePaths = locConfig.SourcePaths.Count > 0
            ? locConfig.SourcePaths
            : AutoDetectPaths(config.ModPaths, locConfig.ResourceType);

        if (sourcePaths.Count == 0)
        {
            _indexService.ApplyLocalisation(new TranslationDatabaseLocalisationIndex([eawDb, focDb], language!));
            return;
        }

        var wsDb = _factory.CreateKeyed([language!]);
        foreach (var path in sourcePaths)
            await TryImportFileAsync(path, locConfig.ResourceType, language!, wsDb, ct);

        _indexService.ApplyLocalisation(new TranslationDatabaseLocalisationIndex([eawDb, focDb, wsDb], language!));
    }

    private IReadOnlyList<string> AutoDetectPaths(IReadOnlyList<string> modPaths, string resourceType)
    {
        var ext = ResourceTypeToExtension(resourceType);
        var results = new List<string>();
        foreach (var modPath in modPaths)
        {
            var textDir = _fileHelper.FileSystem.Path.Combine(modPath, "Data", "Text");
            if (!_fileHelper.FileSystem.Directory.Exists(textDir)) continue;
            var files = _fileHelper.FileSystem.Directory
                .EnumerateFiles(textDir, $"*{ext}", SearchOption.TopDirectoryOnly);
            results.AddRange(files);
        }

        return results;
    }

    private async Task TryImportFileAsync(
        string path, string resourceType, IAlamoLanguageDefinition language,
        IKeyedTranslationDatabase db, CancellationToken ct)
    {
        string content;
        try
        {
            content = await _fileHelper.FileSystem.File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read localisation file {Path}", path);
            return;
        }

        try
        {
            switch (resourceType.ToLowerInvariant())
            {
                case "csv":
                    using (var reader = new StringReader(content))
                    {
                        _csvImporter.Import(reader, db);
                    }

                    break;
                case "xml":
                    var xdoc = XDocument.Parse(content);
                    _xmlImporter.Import(xdoc, db);
                    break;
                case "nls":
                    using (var reader = new StringReader(content))
                    {
                        _nlsImporter.Import(reader, language, db);
                    }

                    break;
                case "dat":
                    _logger.LogWarning(
                        "DAT localisation workspace files are not yet supported; skipping {Path}", path);
                    break;
                default:
                    _logger.LogWarning("Unknown localisation ResourceType '{Type}'; skipping {Path}",
                        resourceType, path);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import localisation file {Path}", path);
        }
    }

    private static string ResourceTypeToExtension(string resourceType)
    {
        return resourceType.ToLowerInvariant() switch
        {
            "csv" => ".csv",
            "xml" => ".xml",
            "nls" => ".properties",
            "dat" => ".dat",
            _ => ".csv"
        };
    }
}