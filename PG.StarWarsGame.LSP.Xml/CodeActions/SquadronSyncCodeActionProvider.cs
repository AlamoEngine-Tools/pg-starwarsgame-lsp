// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

internal sealed class SquadronSyncCodeActionProvider : IXmlCodeActionProvider
{
    private readonly IFileHelper _fileHelper;
    private readonly IGameWorkspaceHost _workspaceHost;

    public SquadronSyncCodeActionProvider(IGameWorkspaceHost workspaceHost, IFileHelper fileHelper)
    {
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
    }

    public IEnumerable<CommandOrCodeAction> Handle(XmlCodeActionContext ctx)
    {
        var syncData = ctx.Diagnostic.Data?["squadronSync"];
        if (syncData is null)
            return [];

        var expectedOffsets = syncData.Value<int?>("expectedOffsets") ?? 0;

        var uri = _fileHelper.NormalizeUri(ctx.DocumentUri.ToString());
        if (!_workspaceHost.TryGet(uri, out var trackedDoc))
            return [];

        var hapDoc = XmlUtility.CreateHtmlDocument(trackedDoc.Text);
        var diagnosticLine = ctx.Diagnostic.Range.Start.Line;

        var objectNode = XmlUtility.FindEnclosingElement(hapDoc, diagnosticLine);
        if (objectNode is null)
            return [];

        var offsetNodes = objectNode.ChildNodes
            .Where(n => n.NodeType == HtmlNodeType.Element &&
                        n.Name.Equals("squadron_offsets", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var unitNodes = objectNode.ChildNodes
            .Where(n => n.NodeType == HtmlNodeType.Element &&
                        n.Name.Equals("squadron_units", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var actualOffsets = offsetNodes.Count;
        var indent = GetIndent(trackedDoc.Text, unitNodes.FirstOrDefault() ?? offsetNodes.FirstOrDefault());

        var actions = new List<CommandOrCodeAction>();

        if (actualOffsets < expectedOffsets)
        {
            var missing = expectedOffsets - actualOffsets;
            var insertAfter = offsetNodes.LastOrDefault() ?? unitNodes.LastOrDefault() ?? objectNode;
            var insertHapLine = EndHapLine(insertAfter);
            var insertPos = new Position(insertHapLine, 0);
            var newText = BuildOffsetLines(indent, missing);

            actions.Add(MakeAction(
                $"Add {missing} missing Squadron_Offsets (0, 0, 0)",
                CodeActionKind.QuickFix, true, ctx, insertPos, insertPos, newText));
        }
        else if (actualOffsets > expectedOffsets)
        {
            var excessCount = actualOffsets - expectedOffsets;
            var excessNodes = offsetNodes.Skip(expectedOffsets).ToList();
            var firstExcess = excessNodes[0];
            var lastExcess = excessNodes[^1];
            var removeStart = new Position(firstExcess.Line - 1, 0);
            var removeEnd = new Position(EndHapLine(lastExcess), 0);

            actions.Add(MakeAction(
                $"Remove {excessCount} excess Squadron_Offsets",
                CodeActionKind.QuickFix, true, ctx, removeStart, removeEnd, ""));
        }

        if (offsetNodes.Count > 0)
        {
            var firstOffset = offsetNodes[0];
            var lastOffset = offsetNodes[^1];
            var replaceStart = new Position(firstOffset.Line - 1, 0);
            var replaceEnd = new Position(EndHapLine(lastOffset), 0);
            var newText = BuildOffsetLines(indent, expectedOffsets);

            actions.Add(MakeAction("Sync all Squadron_Offsets to Squadron_Units",
                CodeActionKind.Refactor, false, ctx, replaceStart, replaceEnd, newText));
        }
        else
        {
            var insertAfter = unitNodes.LastOrDefault() ?? objectNode;
            var insertHapLine = EndHapLine(insertAfter);
            var insertPos = new Position(insertHapLine, 0);
            var newText = BuildOffsetLines(indent, expectedOffsets);

            actions.Add(MakeAction("Sync all Squadron_Offsets to Squadron_Units",
                CodeActionKind.Refactor, false, ctx, insertPos, insertPos, newText));
        }

        return actions;
    }

    private static int EndHapLine(HtmlNode node)
    {
        if (node.EndNode is not null && !ReferenceEquals(node.EndNode, node))
            return node.EndNode.Line;
        return node.Line;
    }

    private static string GetIndent(string text, HtmlNode? refNode)
    {
        if (refNode is null)
            return "    ";
        var lines = text.Split('\n');
        var line0 = refNode.Line - 1;
        if (line0 < 0 || line0 >= lines.Length)
            return "    ";
        var line = lines[line0];
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return line[..i];
    }

    private static string BuildOffsetLines(string indent, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
            sb.Append(indent).Append("<Squadron_Offsets>0, 0, 0</Squadron_Offsets>\n");
        return sb.ToString();
    }

    private static CommandOrCodeAction MakeAction(
        string title, CodeActionKind kind, bool isPreferred,
        XmlCodeActionContext ctx, Position rangeStart, Position rangeEnd, string newText)
    {
        return new CommandOrCodeAction(new CodeAction
        {
            Title = title,
            Kind = kind,
            IsPreferred = isPreferred,
            Diagnostics = new Container<Diagnostic>(ctx.Diagnostic),
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [ctx.DocumentUri] =
                    [
                        new TextEdit
                        {
                            Range = new LspRange(rangeStart, rangeEnd),
                            NewText = newText
                        }
                    ]
                }
            }
        });
    }
}