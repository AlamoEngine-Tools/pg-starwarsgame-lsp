// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Xml.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

/// <summary>
///     Offers a "Remove redundant override" quick fix for variant tag overrides that merely
///     repeat the inherited base value. The fix deletes the whole line of the redundant tag.
///     Triggered by the <c>removeRedundantOverride</c> marker on the diagnostic's data payload,
///     emitted by <c>VariantRedundantOverrideHandler</c>.
/// </summary>
internal sealed class RemoveRedundantOverrideCodeActionProvider : IXmlCodeActionProvider
{
    private readonly IFileHelper _fileHelper;
    private readonly IXmlParseCache _parseCache;

    public RemoveRedundantOverrideCodeActionProvider(IXmlParseCache parseCache, IFileHelper fileHelper)
    {
        _parseCache = parseCache;
        _fileHelper = fileHelper;
    }

    public IEnumerable<CommandOrCodeAction> Handle(XmlCodeActionContext ctx)
    {
        if (ctx.Diagnostic.Data?["removeRedundantOverride"] is null)
            return [];

        var uri = _fileHelper.NormalizeUri(ctx.DocumentUri.ToString());
        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return [];

        var at = ctx.Diagnostic.Range.Start;
        if (parsed.ElementAt(at.Line, at.Character) is not { } node
            || parsed.RemovalRange(node) is not var (start, end))
            return [];

        var removeStart = new Position(start.Line, start.Col);
        var removeEnd = new Position(end.Line, end.Col);
        var tagName = XmlUtility.GetOriginalTagName(node, parsed.Text);

        return
        [
            new CommandOrCodeAction(new CodeAction
            {
                Title = ctx.Messages.Format("RemoveRedundantOverrideAction_RemoveRedundantOverride", tagName),
                Kind = CodeActionKind.QuickFix,
                IsPreferred = true,
                Diagnostics = new Container<Diagnostic>(ctx.Diagnostic),
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [ctx.DocumentUri] =
                        [
                            new TextEdit
                            {
                                Range = new LspRange(removeStart, removeEnd),
                                NewText = ""
                            }
                        ]
                    }
                }
            })
        ];
    }
}
