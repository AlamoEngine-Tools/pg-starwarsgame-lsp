// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class StoryFactProducer(ISchemaProvider schema) : IStoryFactProducer
{
    private const int MaxEventParamSlots = 7;
    private const int MaxRewardParamSlots = 14;

    public IReadOnlyList<XmlFact> Produce(ParsedXmlDocument document, string documentUri)
    {
        var facts = new List<XmlFact>();
        var doc = document.Html;

        foreach (var eventNode in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 string.Equals(n.Name, "Event", StringComparison.OrdinalIgnoreCase)))
            CollectForEvent(eventNode, document.LineIndex, documentUri, facts);

        return facts;
    }

    // Every fact marks the element that caused it, from the document's own offsets (HAP's per-node
    // line and column are unreliable for nested elements): a type problem sits on the type's
    // value, a param problem on the param's value, a MISSING param on the whole type element that
    // demands it, a dialog problem on the dialog's value. Column 0 of a line is never an anchor.
    private void CollectForEvent(HtmlNode eventNode, LineOffsetIndex lineIndex, string documentUri,
        List<XmlFact> facts)
    {
        var eventTypeNode = FindChild(eventNode, "Event_Type");
        if (eventTypeNode is not null)
        {
            var eventType = eventTypeNode.InnerText.Trim();
            if (eventType.Length > 0)
            {
                var def = schema.GetEnum("StoryEventType")?.Values
                    .FirstOrDefault(v => string.Equals(v.Name, eventType, StringComparison.OrdinalIgnoreCase));
                var (line, column, length) = XmlUtility.GetValuePosition(eventTypeNode, lineIndex);
                facts.Add(new StoryEventFact(documentUri, line, column, length, eventType, false, def));
                if (def is not null)
                    CollectParamFacts(eventNode, eventTypeNode, lineIndex, eventType, false,
                        "Event_Param", MaxEventParamSlots, def.Params, documentUri, facts);
            }
        }

        var rewardTypeNode = FindChild(eventNode, "Reward_Type");
        if (rewardTypeNode is not null)
        {
            var rewardType = rewardTypeNode.InnerText.Trim();
            if (rewardType.Length > 0)
            {
                var def = schema.GetEnum("StoryRewardType")?.Values
                    .FirstOrDefault(v => string.Equals(v.Name, rewardType, StringComparison.OrdinalIgnoreCase));
                var (line, column, length) = XmlUtility.GetValuePosition(rewardTypeNode, lineIndex);
                facts.Add(new StoryEventFact(documentUri, line, column, length, rewardType, true, def));
                if (def is not null)
                    CollectParamFacts(eventNode, rewardTypeNode, lineIndex, rewardType, true,
                        "Reward_Param", MaxRewardParamSlots, def.Params, documentUri, facts);
            }
        }

        CollectDialogRefFact(eventNode, lineIndex, documentUri, facts);
    }

    private static void CollectDialogRefFact(HtmlNode eventNode, LineOffsetIndex lineIndex, string documentUri,
        List<XmlFact> facts)
    {
        var dialogNode = FindChild(eventNode, "Story_Dialog");
        var dialogName = dialogNode?.InnerText.Trim();
        if (string.IsNullOrEmpty(dialogName)) return;

        // A non-numeric Story_Chapter is already flagged by the Int tag validation; the
        // cross-check only cares about chapters it can actually look up.
        var chapterNode = FindChild(eventNode, "Story_Chapter");
        int? chapter = null;
        var (chapterLine, chapterColumn, chapterLength) = (-1, -1, 0);
        if (chapterNode is not null && int.TryParse(chapterNode.InnerText.Trim(), out var parsed))
        {
            chapter = parsed;
            (chapterLine, chapterColumn, chapterLength) = XmlUtility.GetValuePosition(chapterNode, lineIndex);
        }

        var (line, column, length) = XmlUtility.GetValuePosition(dialogNode!, lineIndex);
        facts.Add(new StoryDialogRefFact(documentUri, line, column, length,
            dialogName, chapter, chapterLine, chapterColumn, chapterLength));
    }

    /// <summary>
    ///     A whole element's span, opening bracket through closing bracket, from the document's own
    ///     offsets. Same-line elements carry a length; one that wraps carries an end position.
    /// </summary>
    private static (int Line, int Column, int Length, int? EndLine, int? EndColumn) ElementSpan(
        HtmlNode node, LineOffsetIndex lineIndex)
    {
        var (line, column) = lineIndex.GetPosition(node.StreamPosition);
        var (endLine, endColumn) = lineIndex.GetPosition(node.StreamPosition + node.OuterHtml.Length);
        return endLine == line
            ? (line, column, endColumn - column, null, null)
            : (line, column, 0, endLine, endColumn);
    }

    private static void CollectParamFacts(
        HtmlNode eventNode,
        HtmlNode typeNode,
        LineOffsetIndex lineIndex,
        string eventType,
        bool isReward,
        string prefix,
        int maxSlots,
        IReadOnlyList<ParamDefinition>? paramDefs,
        string documentUri,
        List<XmlFact> facts)
    {
        if (paramDefs is null)
            return;

        var maxDefinedPos = paramDefs.Count > 0 ? paramDefs.Max(p => p.Position) : -1;

        for (var n = 1; n <= maxSlots; n++)
        {
            var child = FindChild(eventNode, $"{prefix}{n}");
            if (child is null) continue;
            var value = child.InnerText.Trim();
            if (value.Length == 0) continue;

            var schemaPos = n - 1;
            var (line, column, length) = XmlUtility.GetValuePosition(child, lineIndex);

            if (schemaPos > maxDefinedPos)
            {
                facts.Add(new StoryParamFact(documentUri, line, column, length,
                    eventType, isReward, schemaPos, null, value));
            }
            else
            {
                var paramDef = paramDefs.FirstOrDefault(p => p.Position == schemaPos);
                if (paramDef is null) continue;
                facts.Add(new StoryParamFact(documentUri, line, column, length,
                    eventType, isReward, schemaPos, paramDef, value));
            }
        }

        // A missing param has no element of its own: the type that demands it is what to look at.
        var (typeLine, typeColumn, typeLength, typeEndLine, typeEndColumn) = ElementSpan(typeNode, lineIndex);
        foreach (var p in paramDefs.Where(pd => !pd.Optional))
        {
            var child = FindChild(eventNode, $"{prefix}{p.Position + 1}");
            if (child is not null && child.InnerText.Trim().Length > 0) continue;
            facts.Add(new StoryParamFact(documentUri, typeLine, typeColumn, typeLength,
                eventType, isReward, p.Position, p, "") { EndLine = typeEndLine, EndColumn = typeEndColumn });
        }
    }

    private static HtmlNode? FindChild(HtmlNode parent, string tagName)
    {
        return parent.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            string.Equals(n.Name, tagName, StringComparison.OrdinalIgnoreCase));
    }
}