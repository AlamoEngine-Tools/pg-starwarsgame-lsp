// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Xml.CodeActions;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlCodeActionHandler : CodeActionHandlerBase
{
    private readonly IMessageCatalog _messages;
    private readonly IReadOnlyList<IXmlCursorCodeActionProvider> _cursorProviders;
    private readonly IReadOnlyList<IXmlCodeActionProvider> _providers;

    // Every provider answers every diagnostic, in registration order.
    public XmlCodeActionHandler(IEnumerable<IXmlCodeActionProvider> providers, IMessageCatalog messages,
        IEnumerable<IXmlCursorCodeActionProvider> cursorProviders)
    {
        _providers = [.. providers];
        _messages = messages;
        _cursorProviders = cursorProviders.ToList();
    }

    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;
        var actions = request.Context.Diagnostics
            .Select(d => new XmlCodeActionContext(uri, d) { Messages = _messages })
            .SelectMany(ctx => _providers.SelectMany(p => p.Handle(ctx)))
            .Concat(_cursorProviders.SelectMany(p => p.Handle(uri, request.Range.Start)))
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