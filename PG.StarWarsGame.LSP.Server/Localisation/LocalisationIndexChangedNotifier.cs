// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Notifications;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class LocalisationIndexChangedNotifier
{
    private readonly ILogger<LocalisationIndexChangedNotifier> _logger;
    private readonly Action<string> _sendNotification;
    private readonly StartupHeldNotification _startup;

    public LocalisationIndexChangedNotifier(
        IGameIndexService indexService,
        IStartupGate gate,
        Action<string> sendNotification,
        ILogger<LocalisationIndexChangedNotifier> logger)
    {
        _sendNotification = sendNotification;
        _logger = logger;
        // Startup applies localisation more than once (baseline, then the project's own); the
        // panel is told once, when the gate opens.
        _startup = new StartupHeldNotification(gate, Send);
        // Scoped to localisation-only changes - NOT the general IndexChanged, which also fires
        // for unrelated XML/Lua/asset edits and would otherwise reset the editor panel's UI state
        // on every unrelated workspace change.
        indexService.LocalisationChanged += OnLocalisationChanged;
    }

    private void OnLocalisationChanged(ILocalisationIndex _)
    {
        if (_startup.PassesNow()) Send();
    }

    private void Send()
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