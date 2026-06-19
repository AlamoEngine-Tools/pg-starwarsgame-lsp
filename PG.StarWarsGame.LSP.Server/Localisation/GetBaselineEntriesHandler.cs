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

    public GetBaselineEntriesHandler(
        IBaselineTranslationProvider baselineProvider,
        ILanguageService langService,
        ITranslationDatabaseFactory factory)
    {
        _baselineProvider = baselineProvider;
        _langService = langService;
        _factory = factory;
    }

    public Task<GetBaselineEntriesResult> Handle(
        GetBaselineEntriesParams request, CancellationToken ct)
    {
        var languages = _langService.OfficiallySupported();
        var eawDb = _baselineProvider.GetMasterText(GameContext.EaW, languages);
        var focDb = _baselineProvider.GetMasterText(GameContext.FoC, languages);

        var merged = _factory.CreateKeyed(languages);
        foreach (var entry in eawDb)
        foreach (var kv in entry.Translations)
            merged.SetTranslation(entry.Key, kv.Key, kv.Value);
        foreach (var entry in focDb)
        foreach (var kv in entry.Translations)
            merged.SetTranslation(entry.Key, kv.Key, kv.Value);

        var entries = merged
            .Select(e => new BaselineEntry(
                e.Key,
                e.Translations.ToDictionary(kv => kv.Key.LanguageIdentifier, kv => kv.Value)))
            .ToList();

        return Task.FromResult(new GetBaselineEntriesResult(entries));
    }
}