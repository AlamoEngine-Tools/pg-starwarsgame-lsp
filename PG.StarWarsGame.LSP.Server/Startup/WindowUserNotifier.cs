// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     <see cref="IUserNotifier" /> backed by the LSP client window. Sends <c>window/showMessage</c>
///     so the message appears as a notification in the editor.
/// </summary>
public sealed class WindowUserNotifier : IUserNotifier
{
    private readonly ILanguageServerFacade _facade;
    private readonly ILogger<WindowUserNotifier> _logger;

    public WindowUserNotifier(ILanguageServerFacade facade, ILogger<WindowUserNotifier> logger)
    {
        _facade = facade;
        _logger = logger;
    }

    public void ShowError(string message)
    {
        try
        {
            _facade.Window.ShowError(message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show error notification to the client (non-fatal).");
        }
    }
}
