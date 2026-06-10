// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Xml.CodeActions;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlCodeActionHandler : CodeActionHandlerBase
{
    private readonly IXmlCodeActionRegistry _registry;

    public XmlCodeActionHandler(IXmlCodeActionRegistry registry)
    {
        _registry = registry;
    }

    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;
        var actions = request.Context.Diagnostics
            .SelectMany(d => _registry.Dispatch(new XmlCodeActionContext(uri, d)))
            .ToList();

        return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer(actions));
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CodeActionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml"),
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix),
            ResolveProvider = false
        };
    }
}
