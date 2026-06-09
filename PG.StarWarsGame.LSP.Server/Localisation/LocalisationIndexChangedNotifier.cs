// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class LocalisationIndexChangedNotifier
{
    private readonly Action<string> _sendNotification;
    private readonly ILogger<LocalisationIndexChangedNotifier> _logger;

    public LocalisationIndexChangedNotifier(
        IGameIndexService indexService,
        Action<string> sendNotification,
        ILogger<LocalisationIndexChangedNotifier> logger)
    {
        _sendNotification = sendNotification;
        _logger = logger;
        indexService.IndexChanged += OnIndexChanged;
    }

    private void OnIndexChanged(GameIndex _)
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
