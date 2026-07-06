// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

/// <summary>
///     Offers a "Remove earlier duplicate occurrences" quick fix for duplicate singleton tags
///     within one object. The game reads objects top to bottom and the LAST occurrence wins, so
///     the fix deletes every same-named earlier sibling (whole lines), simulating exactly what the
///     engine effectively does at load time. Triggered by the <c>removeEarlierDuplicates</c>
///     marker on the diagnostic's data payload, emitted by <c>XmlDuplicateTagHandler</c>.
/// </summary>
internal sealed class RemoveEarlierDuplicatesCodeActionProvider : IXmlCodeActionProvider
{
    private readonly IFileHelper _fileHelper;
    private readonly IXmlParseCache _parseCache;

    public RemoveEarlierDuplicatesCodeActionProvider(IXmlParseCache parseCache, IFileHelper fileHelper)
    {
        _parseCache = parseCache;
        _fileHelper = fileHelper;
    }

    public IEnumerable<CommandOrCodeAction> Handle(XmlCodeActionContext ctx)
    {
        if (ctx.Diagnostic.Data?["removeEarlierDuplicates"] is null)
            return [];

        var uri = _fileHelper.NormalizeUri(ctx.DocumentUri.ToString());
        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return [];

        var hapDoc = parsed.Html;
        var diagnosticLine = ctx.Diagnostic.Range.Start.Line;
        if (!XmlUtility.TryFindNode(hapDoc, diagnosticLine, out var node) || node?.ParentNode is null)
            return [];

        // All same-named siblings in document order; everything BEFORE the last one is dead
        // weight the engine ignores.
        var occurrences = node.ParentNode.ChildNodes
            .Where(n => n.NodeType == HtmlNodeType.Element &&
                        n.Name.Equals(node.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (occurrences.Count < 2)
            return [];

        var edits = occurrences[..^1]
            .Select(n => new TextEdit
            {
                Range = new LspRange(new Position(n.Line - 1, 0), new Position(EndHapLine(n), 0)),
                NewText = ""
            })
            .ToList();

        var tagName = XmlUtility.GetOriginalTagName(node, parsed.Text);
        var count = occurrences.Count - 1;

        return
        [
            new CommandOrCodeAction(new CodeAction
            {
                Title = count == 1
                    ? $"Remove the earlier duplicate '{tagName}' (the game keeps the last occurrence)"
                    : $"Remove the {count} earlier duplicate '{tagName}' occurrences (the game keeps the last)",
                Kind = CodeActionKind.QuickFix,
                IsPreferred = true,
                Diagnostics = new Container<Diagnostic>(ctx.Diagnostic),
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [ctx.DocumentUri] = edits
                    }
                }
            })
        ];
    }

    private static int EndHapLine(HtmlNode node)
    {
        if (node.EndNode is not null && !ReferenceEquals(node.EndNode, node))
            return node.EndNode.Line;
        return node.Line;
    }
}
