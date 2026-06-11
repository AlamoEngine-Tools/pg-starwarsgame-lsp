// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Second pipeline stage: loads the shipped-game baseline index and baseline localisation keys
///     from the configured source and applies them to the <see cref="IGameIndexService" />. Each
///     part degrades independently — a failed baseline becomes <see cref="BaselineIndex.Empty" /> and
///     a failed localisation load simply leaves baseline keys unavailable — so startup always
///     continues.
/// </summary>
public sealed class BaselineBootstrapper : IBaselineBootstrapper
{
    private readonly BaselineLoader _baselineLoader;
    private readonly ILspConfigurationProvider _config;
    private readonly IGameIndexService _indexService;
    private readonly ILanguageService _languageService;
    private readonly IBaselineTranslationProvider _localisationProvider;
    private readonly ILogger<BaselineBootstrapper> _logger;

    public BaselineBootstrapper(
        ILspConfigurationProvider config,
        IGameIndexService indexService,
        BaselineLoader baselineLoader,
        IBaselineTranslationProvider localisationProvider,
        ILanguageService languageService,
        ILogger<BaselineBootstrapper> logger)
    {
        _config = config;
        _indexService = indexService;
        _baselineLoader = baselineLoader;
        _localisationProvider = localisationProvider;
        _languageService = languageService;
        _logger = logger;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        BaselineIndex baseline;
        try
        {
            baseline = await _baselineLoader.LoadAsync(_config.Current.BaselineSource, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Baseline load failed; using empty baseline");
            baseline = BaselineIndex.Empty;
        }

        _indexService.ApplyBaseline(baseline);

        try
        {
            if (!_languageService.TryGetByIdentifier(_config.Current.Locale, out var language))
                language = _languageService.Default;
            var eawDb = _localisationProvider.GetMasterText(GameContext.EaW, language!);
            var focDb = _localisationProvider.GetMasterText(GameContext.FoC, language!);
            _indexService.ApplyLocalisation(
                new TranslationDatabaseLocalisationIndex([eawDb, focDb], language!));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Baseline localisation load failed; localisation keys unavailable");
        }
    }
}