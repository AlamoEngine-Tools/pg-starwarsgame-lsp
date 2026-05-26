// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlCodeActionHandler : CodeActionHandlerBase
{
    private readonly IXmlFixCache _fixCache;

    public XmlCodeActionHandler(IXmlFixCache fixCache)
    {
        _fixCache = fixCache;
    }

    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;
        var uriString = uri.ToString();

        var actions = request.Context.Diagnostics
            .Select(d =>
            {
                // Prefer data echoed back by the client; fall back to server-side cache
                var fix = d.Data?["fix"]?.Value<string>()
                          ?? _fixCache.GetSuggestedFix(uriString, d.Range.Start.Line, d.Range.Start.Character);
                return fix is not null ? (CommandOrCodeAction?)BuildAction(uri, d, fix) : null;
            })
            .Where(a => a is not null)
            .Cast<CommandOrCodeAction>();

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

    private static CodeAction BuildAction(DocumentUri docUri, Diagnostic d, string fix)
    {
        return new CodeAction
        {
            Title = $"Replace with '{fix}'",
            Kind = CodeActionKind.QuickFix,
            Diagnostics = new Container<Diagnostic>(d),
            IsPreferred = true,
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [docUri] = [new TextEdit { Range = d.Range, NewText = fix }]
                }
            }
        };
    }
}