// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>Payload of <c>$/workspaceScanComplete</c>: the projects the session ended up serving.</summary>
public sealed class WorkspaceScanCompleteParams
{
    public IReadOnlyList<ScannedProject> Projects { get; init; } = [];
}

public sealed class ScannedProject
{
    public string Name { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
}

/// <summary>
///     Fires the one-shot client signals emitted when the startup pipeline finishes and the gate is
///     open: the <c>$/workspaceScanComplete</c> notification plus codeLens and inlayHint refreshes.
///     VS Code requests codeLens/inlayHint once, when a document opens - which falls inside the
///     startup buffering window while the index is still empty, so the results come back blank. The
///     refreshes tell the client to re-request them against the now-populated index.
/// </summary>
public sealed class StartupNotifier : IStartupNotifier
{
    private readonly ILanguageServerFacade _facade;
    private readonly ILogger<StartupNotifier> _logger;
    private readonly IClientRefreshNotifier _refresh;
    private readonly IProjectRegistry? _registry;

    // registry is optional so the minimal test setups can omit it; production always wires it.
    public StartupNotifier(ILanguageServerFacade facade, IClientRefreshNotifier refresh,
        ILogger<StartupNotifier> logger, IProjectRegistry? registry = null)
    {
        _facade = facade;
        _refresh = refresh;
        _logger = logger;
        _registry = registry;
    }

    public void NotifyScanComplete()
    {
        _logger.LogInformation("Notifying workspace scan complete.");
        try
        {
            // The resolved projects travel with the notification so the client can say how many
            // mods it is serving without a follow-up round trip.
            _facade.SendNotification("$/workspaceScanComplete", new WorkspaceScanCompleteParams
            {
                Projects = _registry?.All
                    .Where(w => w.Id is not null)
                    .Select(w => new ScannedProject
                    {
                        Name = w.Configuration.Layers.Count > 0
                            ? w.Configuration.Layers[^1].Name
                            : w.Id!,
                        ProjectPath = w.Id!
                    })
                    .ToList() ?? []
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send $/workspaceScanComplete (non-fatal)");
        }

        // VS Code requests codeLens and inlayHint once, when a document opens - which happens
        // during the startup buffering window while the index is still empty, so those requests
        // return nothing. Tell the client to re-request them now that the index is populated.
        _refresh.RefreshDerivedState();
        _logger.LogInformation("Requested client refresh of inlay hints and code lenses after scan.");
    }
}