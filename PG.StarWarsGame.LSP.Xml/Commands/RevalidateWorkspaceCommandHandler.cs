// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Commands;

/// <summary>
///     Backs "EaWEdit: Re-validate Workspace". Refreshes every language's diagnostics, which is what
///     the command has always claimed to do - it depended on the XML revalidator alone, from when
///     XML was the only language that published diagnostics.
/// </summary>
public sealed class RevalidateWorkspaceCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet-eaw-edit.lsp.revalidateWorkspace";

    private readonly ILogger<RevalidateWorkspaceCommandHandler> _logger;
    private readonly IEnumerable<IDiagnosticsRepublisher> _republishers;

    public RevalidateWorkspaceCommandHandler(
        IEnumerable<IDiagnosticsRepublisher> republishers,
        ILogger<RevalidateWorkspaceCommandHandler>? logger = null)
    {
        _republishers = republishers;
        _logger = logger ?? NullLogger<RevalidateWorkspaceCommandHandler>.Instance;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        // One language failing is not a reason to abandon the sweep: the user asked for a refresh,
        // and a partial one beats leaving the rest of the workspace stale as well.
        foreach (var republisher in _republishers)
            try
            {
                await republisher.RepublishAllAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Cmd}: {Publisher} failed to revalidate.",
                    CommandName, republisher.GetType().Name);
            }
    }

    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        await ExecuteAsync(ct);
        return Unit.Value;
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions { Commands = new Container<string>(CommandName) };
    }
}
