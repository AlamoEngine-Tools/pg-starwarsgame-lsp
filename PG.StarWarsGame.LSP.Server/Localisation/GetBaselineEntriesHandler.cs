// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Localisation.Rows;
using PG.StarWarsGame.LSP.Localisation;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class GetBaselineEntriesHandler
    : IJsonRpcRequestHandler<GetBaselineEntriesParams, GetBaselineEntriesResult>
{
    private readonly IBaselineTranslationProvider _baselineProvider;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IFileHelper _fileHelper;
    private readonly ILanguageService _langService;
    private readonly ILocalisationLayerRegistry _layerRegistry;
    private readonly ILocalisationProjectRegistry _projectRegistry;
    private readonly IModProjectReloadService _project;

    public GetBaselineEntriesHandler(
        IBaselineTranslationProvider baselineProvider,
        ILanguageService langService,
        ITranslationDatabaseFactory factory,
        IFileHelper fileHelper,
        ILocalisationProjectRegistry projectRegistry,
        ILocalisationLayerRegistry layerRegistry,
        IModProjectReloadService project)
    {
        _baselineProvider = baselineProvider;
        _langService = langService;
        _factory = factory;
        _fileHelper = fileHelper;
        _projectRegistry = projectRegistry;
        _layerRegistry = layerRegistry;
        _project = project;
    }

    public Task<GetBaselineEntriesResult> Handle(
        GetBaselineEntriesParams request, CancellationToken ct)
    {
        var languages = _langService.OfficiallySupported();
        // The shipped text of the project's game only: the base game ships none of the expansion's.
        var games = BaselineMasterTexts.Contexts(_project.Game);

        // A credits file gets the game's own credits, not its MasterText. The two share nothing: a
        // credits "key" is a formatting directive repeated on hundreds of rows, so the caller
        // matches these entries by their text rather than by key. Flattened as they come, with no
        // layer merge - inheritance is a keyed idea and means nothing for an ordered list.
        // The path is optional on this request - without one there is no file to classify, and the
        // MasterText baseline is the only sensible answer.
        if (!string.IsNullOrWhiteSpace(request.ProjectFilePath)
            && LocalisationCategoryResolver.Resolve(
                _projectRegistry, _fileHelper, request.ProjectFilePath) == LocCategory.Credits)
        {
            var credits = new List<BaselineEntry>();
            foreach (var db in games.Select(g => _baselineProvider.GetCreditsText(g, languages)))
            foreach (var entry in db)
                credits.Add(new BaselineEntry(
                    entry.Key,
                    entry.Translations.ToDictionary(kv => kv.Key.LanguageIdentifier, kv => kv.Value)));

            return Task.FromResult(new GetBaselineEntriesResult(credits));
        }

        var merged = _factory.CreateKeyed(languages);
        LocalisationLayerMerge.MergeBaselineAndLowerLayers(
            merged, [.. games.Select(g => _baselineProvider.GetMasterText(g, languages))], _layerRegistry.Layers,
            ResolveBelowRank(request.ProjectFilePath));

        var entries = merged
            .Select(e => new BaselineEntry(
                e.Key,
                e.Translations.ToDictionary(kv => kv.Key.LanguageIdentifier, kv => kv.Value)))
            .ToList();

        return Task.FromResult(new GetBaselineEntriesResult(entries));
    }

    // The rank of the layer that owns projectFilePath - everything strictly below it (dependency
    // layers) is "inherited" for that file. Null (baseline only) when no file was specified or it
    // isn't a currently registered localisation project file.
    private int? ResolveBelowRank(string? projectFilePath)
    {
        if (string.IsNullOrEmpty(projectFilePath)) return null;
        var project = _projectRegistry.Projects.FirstOrDefault(p =>
            string.Equals(p.FilePath, projectFilePath, StringComparison.OrdinalIgnoreCase));
        return project?.Rank;
    }
}
