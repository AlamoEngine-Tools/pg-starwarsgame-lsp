// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class GetBaselineEntriesHandler
    : IJsonRpcRequestHandler<GetBaselineEntriesParams, GetBaselineEntriesResult>
{
    private readonly IBaselineTranslationProvider _baselineProvider;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly ILanguageService _langService;
    private readonly ILocalisationLayerRegistry _layerRegistry;
    private readonly ILocalisationProjectRegistry _projectRegistry;

    public GetBaselineEntriesHandler(
        IBaselineTranslationProvider baselineProvider,
        ILanguageService langService,
        ITranslationDatabaseFactory factory,
        ILocalisationProjectRegistry projectRegistry,
        ILocalisationLayerRegistry layerRegistry)
    {
        _baselineProvider = baselineProvider;
        _langService = langService;
        _factory = factory;
        _projectRegistry = projectRegistry;
        _layerRegistry = layerRegistry;
    }

    public Task<GetBaselineEntriesResult> Handle(
        GetBaselineEntriesParams request, CancellationToken ct)
    {
        var languages = _langService.OfficiallySupported();
        var eawDb = _baselineProvider.GetMasterText(GameContext.EaW, languages);
        var focDb = _baselineProvider.GetMasterText(GameContext.FoC, languages);

        var merged = _factory.CreateKeyed(languages);
        LocalisationLayerMerge.MergeBaselineAndLowerLayers(
            merged, [eawDb, focDb], _layerRegistry.Layers, ResolveBelowRank(request.ProjectFilePath));

        var entries = merged
            .Select(e => new BaselineEntry(
                e.Key,
                e.Translations.ToDictionary(kv => kv.Key.LanguageIdentifier, kv => kv.Value)))
            .ToList();

        return Task.FromResult(new GetBaselineEntriesResult(entries));
    }

    // The rank of the layer that owns projectFilePath — everything strictly below it (dependency
    // layers) is "inherited" for that file. Null (baseline only) when no file was specified or it
    // isn't a currently registered localisation project file.
    private int? ResolveBelowRank(string? projectFilePath)
    {
        if (string.IsNullOrEmpty(projectFilePath)) return null;
        var project = _projectRegistry.Projects.FirstOrDefault(
            p => string.Equals(p.FilePath, projectFilePath, StringComparison.OrdinalIgnoreCase));
        return project?.Rank;
    }
}