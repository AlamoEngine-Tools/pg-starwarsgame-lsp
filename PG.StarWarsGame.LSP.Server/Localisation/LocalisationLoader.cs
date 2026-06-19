// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

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
    private readonly LocalisationProjectRegistry _registry;
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
        LocalisationProjectRegistry registry,
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
        _registry = registry;
        _logger = logger;
    }

    public async Task LoadAsync(WorkspaceConfiguration workspaceConfig, CancellationToken ct)
    {
        var config = _configProvider.Current;
        var locConfig = config.Localisation;

        if (!_langService.TryGetByIdentifier(config.Locale, out var language))
            language = _langService.Default;

        var eawDb = _baselineProvider.GetMasterText(GameContext.EaW, language!);
        var focDb = _baselineProvider.GetMasterText(GameContext.FoC, language!);

        var registryEntries = new List<LocProjectInfo>();
        var layerDbs = new List<IKeyedTranslationDatabase>();

        // pgproj mode is driven by the resolved project layers (or flat TextRoots as a single
        // synthetic layer). Each layer is loaded with its OWN resource type so a dependency's CSV is
        // never skipped because the root project declares a different type. Higher layers come first
        // so they win under the index's first-wins lookup.
        var textLayers = ResolveTextLayers(workspaceConfig);
        if (textLayers.Count > 0)
        {
            foreach (var layer in textLayers)
            {
                var layerType = layer.TextResourceType ?? locConfig.ResourceType;
                var paths = EnumerateFromTextRoots(layer.TextRoots, layerType);
                if (paths.Count == 0) continue;

                var db = _factory.CreateKeyed([language!]);
                foreach (var path in paths)
                {
                    await TryImportFileAsync(path, layerType, language!, db, ct);
                    registryEntries.Add(new LocProjectInfo(Path.GetFileName(path), path, layerType));
                }

                layerDbs.Add(db);
            }
        }
        else
        {
            // Fallback for a workspace with no resolved project: honour explicitly configured paths.
            var resourceType = locConfig.ResourceType;
            var sourcePaths = locConfig.SourcePaths;

            if (sourcePaths.Count > 0)
            {
                var db = _factory.CreateKeyed([language!]);
                foreach (var path in sourcePaths)
                {
                    await TryImportFileAsync(path, resourceType, language!, db, ct);
                    registryEntries.Add(new LocProjectInfo(Path.GetFileName(path), path, resourceType));
                }

                layerDbs.Add(db);
            }
        }

        _registry.Set(registryEntries);

        // Workspace layers (highest precedence first) shadow the shipped baseline.
        var databases = new List<IKeyedTranslationDatabase>(layerDbs) { eawDb, focDb };
        _indexService.ApplyLocalisation(new TranslationDatabaseLocalisationIndex(databases, language!));
    }

    // Project layers that carry text, highest precedence first. Falls back to a single synthetic
    // layer when only flat TextRoots are present (older callers / no resolved layers). An empty
    // result means heuristic mode.
    private static IReadOnlyList<ProjectLayer> ResolveTextLayers(WorkspaceConfiguration workspaceConfig)
    {
        if (workspaceConfig.Layers.Count > 0)
            return workspaceConfig.Layers.OrderByDescending(l => l.Rank).ToList();

        if (workspaceConfig.TextRoots.Count > 0)
            return
            [
                new ProjectLayer(0, "workspace", [], [], workspaceConfig.TextRoots, [],
                    workspaceConfig.TextResourceType)
            ];

        return [];
    }

    private IReadOnlyList<string> EnumerateFromTextRoots(IReadOnlyList<string> textRoots, string resourceType)
    {
        var ext = ResourceTypeToExtension(resourceType);
        var results = new List<string>();
        foreach (var dir in textRoots)
        {
            if (!_fileHelper.FileSystem.Directory.Exists(dir)) continue;
            results.AddRange(_fileHelper.FileSystem.Directory
                .EnumerateFiles(dir, $"*{ext}", SearchOption.TopDirectoryOnly));
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