// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaCodeActionHandler : CodeActionHandlerBase
{
    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;

        var actions = request.Context.Diagnostics
            .Where(d => d.Code?.IsString == true)
            .Select(d => d.Code!.Value.String switch
            {
                LuaDiagnosticCodes.RedundantRequire => (CommandOrCodeAction?)BuildDeleteLineAction(
                    uri, d, "Remove redundant require"),
                LuaDiagnosticCodes.DuplicateRequire => BuildDeleteLineAction(
                    uri, d, "Remove duplicate require"),
                _ => null
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
            DocumentSelector = TextDocumentSelector.ForLanguage("lua"),
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix),
            ResolveProvider = false
        };
    }

    private static CodeAction BuildDeleteLineAction(DocumentUri docUri, Diagnostic d, string title)
    {
        var line = d.Range.Start.Line;
        var deleteRange = new LspRange(new Position(line, 0), new Position(line + 1, 0));

        return new CodeAction
        {
            Title = title,
            Kind = CodeActionKind.QuickFix,
            Diagnostics = new Container<Diagnostic>(d),
            IsPreferred = true,
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [docUri] = [new TextEdit { Range = deleteRange, NewText = "" }]
                }
            }
        };
    }
}