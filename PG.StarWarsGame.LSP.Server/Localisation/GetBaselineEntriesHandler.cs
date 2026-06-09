// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data.Config.v2;
using PG.StarWarsGame.Localisation.Services;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class GetBaselineEntriesHandler
    : IJsonRpcRequestHandler<GetBaselineEntriesParams, GetBaselineEntriesResult>
{
    private readonly IBaselineTranslationProvider _baselineProvider;
    private readonly ILanguageService _langService;

    public GetBaselineEntriesHandler(
        IBaselineTranslationProvider baselineProvider,
        ILanguageService langService)
    {
        _baselineProvider = baselineProvider;
        _langService = langService;
    }

    public Task<GetBaselineEntriesResult> Handle(
        GetBaselineEntriesParams request, CancellationToken ct)
    {
        var languages = _langService.OfficiallySupported;
        var eawDb = _baselineProvider.GetMasterText(GameType.EaW, languages);
        var focDb = _baselineProvider.GetMasterText(GameType.FoC, languages);

        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var db in new[] { eawDb, focDb })
        {
            foreach (var entry in db)
            {
                if (!merged.TryGetValue(entry.Key, out var trans))
                {
                    trans = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    merged[entry.Key] = trans;
                }
                foreach (var kvp in entry.Translations)
                    trans[kvp.Key.LanguageIdentifier] = kvp.Value;
            }
        }

        var entries = merged
            .Select(kvp => new BaselineEntry(kvp.Key, kvp.Value))
            .ToList();
        return Task.FromResult(new GetBaselineEntriesResult(entries));
    }
}
