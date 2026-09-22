// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Notifications;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     Pushes <c>aet/storyGraphChanged</c> after index changes invalidate campaign models, so the
///     client re-fetches instead of polling. Debounced like the diagnostics publishers: rapid
///     consecutive changes (bulk indexing, workspace-wide rename) produce one notification. Only
///     already-built models are inspected - the notifier never triggers a rebuild itself.
/// </summary>
public sealed class StoryGraphChangeNotifier : DebouncedIndexNotifier
{
    private readonly ILspConfigurationProvider _config;
    private readonly ILogger<StoryGraphChangeNotifier> _logger;
    private readonly IStoryModelService _modelService;
    private readonly Action<StoryGraphChangedParams> _send;

    public StoryGraphChangeNotifier(
        IGameIndexService indexService,
        IStoryModelService modelService,
        ILspConfigurationProvider config,
        Action<StoryGraphChangedParams> send,
        ILogger<StoryGraphChangeNotifier> logger,
        int debounceMs = 100)
        : base(indexService, debounceMs)
    {
        _modelService = modelService;
        _config = config;
        _send = send;
        _logger = logger;
    }

    protected override void Notify()
    {
        try
        {
            if (StoryEditorFeature.Rejection(_config) is not null) return;

            var campaigns = _modelService.GetInvalidatedCampaigns();
            if (campaigns.Count == 0) return;

            _logger.LogDebug("aet/storyGraphChanged: {Count} campaign(s) invalidated", campaigns.Count);
            _send(new StoryGraphChangedParams(campaigns));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send aet/storyGraphChanged");
        }
    }
}