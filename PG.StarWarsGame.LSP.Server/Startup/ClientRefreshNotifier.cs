// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <inheritdoc />
public sealed class ClientRefreshNotifier : IClientRefreshNotifier
{
    private readonly ILanguageServerFacade _facade;
    private readonly ILogger<ClientRefreshNotifier> _logger;

    public ClientRefreshNotifier(ILanguageServerFacade facade, ILogger<ClientRefreshNotifier> logger)
    {
        _facade = facade;
        _logger = logger;
    }

    public void RefreshDerivedState()
    {
        // Access the workspace endpoint via _facade.Workspace; the facade itself is not an
        // IWorkspaceLanguageServer.
        var workspace = _facade.Workspace;

        try
        {
            workspace.SendCodeLensRefresh(new CodeLensRefreshParams());
            _logger.LogDebug("Sent workspace/codeLens/refresh.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "workspace/codeLens/refresh failed (non-fatal)");
        }

        try
        {
            workspace.SendInlayHintRefresh(new InlayHintRefreshParams());
            _logger.LogDebug("Sent workspace/inlayHint/refresh.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "workspace/inlayHint/refresh failed (non-fatal)");
        }
    }
}
