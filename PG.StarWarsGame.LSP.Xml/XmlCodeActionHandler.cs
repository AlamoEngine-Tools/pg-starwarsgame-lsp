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

        var actions = new List<CommandOrCodeAction>();
        foreach (var d in request.Context.Diagnostics)
        {
            var fix = d.Data?["fix"]?.Value<string>()
                      ?? _fixCache.GetSuggestedFix(uriString, d.Range.Start.Line, d.Range.Start.Character);
            if (fix is not null)
                actions.Add(BuildFixAction(uri, d, fix));

            var locKey = d.Data?["createLocKey"]?.Value<string>();
            if (locKey is not null)
                actions.Add(BuildCreateLocKeyAction(d, locKey));
        }

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

    private static CodeAction BuildFixAction(DocumentUri docUri, Diagnostic d, string fix)
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

    private static CodeAction BuildCreateLocKeyAction(Diagnostic d, string keyName)
    {
        return new CodeAction
        {
            Title = $"Create localisation key '{keyName}'",
            Kind = CodeActionKind.QuickFix,
            Diagnostics = new Container<Diagnostic>(d),
            Command = new Command
            {
                Name = "aet-eaw-edit.lsp.createLocalisationKey",
                Title = "Create localisation key",
                Arguments = new JArray(keyName)
            }
        };
    }
}