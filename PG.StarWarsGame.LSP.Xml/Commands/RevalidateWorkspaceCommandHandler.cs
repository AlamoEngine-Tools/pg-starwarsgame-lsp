// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace PG.StarWarsGame.LSP.Xml.Commands;

public sealed class RevalidateWorkspaceCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet.revalidateWorkspace";

    private readonly IXmlDiagnosticsRevalidator _revalidator;

    public RevalidateWorkspaceCommandHandler(IXmlDiagnosticsRevalidator revalidator)
    {
        _revalidator = revalidator;
    }

    public async Task ExecuteAsync(CancellationToken ct)
        => await _revalidator.RevalidateWorkspaceAsync(ct);

    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        await ExecuteAsync(ct);
        return Unit.Value;
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
        => new() { Commands = new Container<string>(CommandName) };
}
