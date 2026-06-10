// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

internal sealed class FixSuggestionCodeActionProvider : IXmlCodeActionProvider
{
    private readonly IXmlFixCache _fixCache;

    public FixSuggestionCodeActionProvider(IXmlFixCache fixCache) => _fixCache = fixCache;

    public IEnumerable<CommandOrCodeAction> Handle(XmlCodeActionContext ctx)
    {
        var d = ctx.Diagnostic;
        var fix = (string?)d.Data?["fix"]
                  ?? _fixCache.GetSuggestedFix(ctx.DocumentUri.ToString(), d.Range.Start.Line, d.Range.Start.Character);
        if (fix is null)
            return [];

        return
        [
            new CommandOrCodeAction(new CodeAction
            {
                Title = $"Replace with '{fix}'",
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<Diagnostic>(d),
                IsPreferred = true,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [ctx.DocumentUri] = [new TextEdit { Range = d.Range, NewText = fix }]
                    }
                }
            })
        ];
    }
}
