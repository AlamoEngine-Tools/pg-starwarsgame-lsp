// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class LocalisationIndexChangedNotifier
{
    private readonly ILogger<LocalisationIndexChangedNotifier> _logger;
    private readonly Action<string> _sendNotification;

    public LocalisationIndexChangedNotifier(
        IGameIndexService indexService,
        Action<string> sendNotification,
        ILogger<LocalisationIndexChangedNotifier> logger)
    {
        _sendNotification = sendNotification;
        _logger = logger;
        // Scoped to localisation-only changes - NOT the general IndexChanged, which also fires
        // for unrelated XML/Lua/asset edits and would otherwise reset the editor panel's UI state
        // on every unrelated workspace change.
        indexService.LocalisationChanged += OnLocalisationChanged;
    }

    private void OnLocalisationChanged(ILocalisationIndex _)
    {
        try
        {
            _sendNotification("aet/localisationIndexUpdated");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send aet/localisationIndexUpdated (non-fatal).");
        }
    }
}