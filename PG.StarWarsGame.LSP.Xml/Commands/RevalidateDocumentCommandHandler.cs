// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace PG.StarWarsGame.LSP.Xml.Commands;

public sealed class RevalidateDocumentCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet.revalidateDocument";

    private readonly IXmlDiagnosticsRevalidator _revalidator;

    public RevalidateDocumentCommandHandler(IXmlDiagnosticsRevalidator revalidator)
    {
        _revalidator = revalidator;
    }

    public async Task ExecuteAsync(string uri, CancellationToken ct)
        => await _revalidator.RevalidateDocumentAsync(uri, ct);

    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        var uri = request.Arguments?[0]?.Value<string>() ?? string.Empty;
        await ExecuteAsync(uri, ct);
        return Unit.Value;
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
        => new() { Commands = new Container<string>(CommandName) };
}
