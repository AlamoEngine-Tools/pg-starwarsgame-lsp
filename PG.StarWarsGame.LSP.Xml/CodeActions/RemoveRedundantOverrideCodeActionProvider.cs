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
///     Offers a "Remove redundant override" quick fix for variant tag overrides that merely
///     repeat the inherited base value. The fix deletes the whole line of the redundant tag.
///     Triggered by the <c>removeRedundantOverride</c> marker on the diagnostic's data payload,
///     emitted by <c>VariantRedundantOverrideHandler</c>.
/// </summary>
internal sealed class RemoveRedundantOverrideCodeActionProvider : IXmlCodeActionProvider
{
    private readonly IFileHelper _fileHelper;
    private readonly IGameWorkspaceHost _workspaceHost;

    public RemoveRedundantOverrideCodeActionProvider(IGameWorkspaceHost workspaceHost, IFileHelper fileHelper)
    {
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
    }

    public IEnumerable<CommandOrCodeAction> Handle(XmlCodeActionContext ctx)
    {
        if (ctx.Diagnostic.Data?["removeRedundantOverride"] is null)
            return [];

        var uri = _fileHelper.NormalizeUri(ctx.DocumentUri.ToString());
        if (!_workspaceHost.TryGet(uri, out var trackedDoc))
            return [];

        var hapDoc = XmlUtility.CreateHtmlDocument(trackedDoc.Text);
        var diagnosticLine = ctx.Diagnostic.Range.Start.Line;
        if (!XmlUtility.TryFindNode(hapDoc, diagnosticLine, out var node) || node is null)
            return [];

        var removeStart = new Position(node.Line - 1, 0);
        var removeEnd = new Position(EndHapLine(node), 0);
        var tagName = XmlUtility.GetOriginalTagName(node, trackedDoc.Text);

        return
        [
            new CommandOrCodeAction(new CodeAction
            {
                Title = $"Remove redundant override '{tagName}'",
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

    private static int EndHapLine(HtmlNode node)
    {
        if (node.EndNode is not null && !ReferenceEquals(node.EndNode, node))
            return node.EndNode.Line;
        return node.Line;
    }
}