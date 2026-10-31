// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Localisation;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Loads the shipped game's baseline index and master text for the project's game from the
///     configured source and applies them to the <see cref="IGameIndexService" />. The pipeline asks
///     first, on the game the project file names; every project load asks again with the game it
///     resolved, and the same game is not loaded twice. Each part degrades independently - a failed
///     baseline becomes <see cref="BaselineIndex.Empty" /> and a failed localisation load leaves
///     baseline keys unavailable - so startup always continues.
/// </summary>
public sealed class BaselineBootstrapper : IBaselineBootstrapper
{
    private readonly BaselineLoader _baselineLoader;
    private readonly ILspConfigurationProvider _config;
    private readonly SemaphoreSlim _gate = new(1, 1);
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

    /// <inheritdoc />
    public TargetGame? LoadedGame { get; private set; }

    public async Task LoadAsync(TargetGame game, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Only a baseline that loaded counts as loaded: an empty one is asked for again on the
            // next project load, where a download that failed at startup may now succeed.
            if (LoadedGame == game) return;
            await LoadCoreAsync(game, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadCoreAsync(TargetGame game, CancellationToken ct)
    {
        BaselineIndex baseline;
        try
        {
            baseline = await _baselineLoader.LoadAsync(_config.Current.BaselineSource.ForGame(game), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Baseline load failed; using empty baseline");
            baseline = BaselineIndex.Empty;
        }

        _indexService.ApplyBaseline(baseline);
        LoadedGame = baseline.Symbols.IsEmpty ? null : game;

        try
        {
            // The game's language, not the LSP's own - see LocalisationLoader for why these are
            // different settings.
            if (!_languageService.TryGetByIdentifier(
                    _config.Current.Localisation.Language, out var language))
                language = _languageService.Default;
            _indexService.ApplyLocalisation(
                new TranslationDatabaseLocalisationIndex(
                    [
                        .. BaselineMasterTexts.Contexts(game)
                            .Select(c => _localisationProvider.GetMasterText(c, language))
                    ],
                    language));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Baseline localisation load failed; localisation keys unavailable");
        }
    }
}