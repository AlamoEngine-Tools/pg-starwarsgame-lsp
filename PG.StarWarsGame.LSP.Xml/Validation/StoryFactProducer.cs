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
            CollectForEvent(eventNode, documentUri, facts);

        return facts;
    }

    private void CollectForEvent(HtmlNode eventNode, string documentUri, List<XmlFact> facts)
    {
        var eventNodeLine = Math.Max(0, eventNode.Line - 1);

        var eventTypeNode = FindChild(eventNode, "Event_Type");
        if (eventTypeNode is not null)
        {
            var eventType = eventTypeNode.InnerText.Trim();
            if (eventType.Length > 0)
            {
                var def = schema.GetEnum("StoryEventType")?.Values
                    .FirstOrDefault(v => string.Equals(v.Name, eventType, StringComparison.OrdinalIgnoreCase));
                facts.Add(new StoryEventFact(documentUri, Math.Max(0, eventTypeNode.Line - 1), 0, 0,
                    eventType, false, def));
                if (def is not null)
                    CollectParamFacts(eventNode, eventNodeLine, eventType, false,
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
                facts.Add(new StoryEventFact(documentUri, Math.Max(0, rewardTypeNode.Line - 1), 0, 0,
                    rewardType, true, def));
                if (def is not null)
                    CollectParamFacts(eventNode, eventNodeLine, rewardType, true,
                        "Reward_Param", MaxRewardParamSlots, def.Params, documentUri, facts);
            }
        }

        CollectDialogRefFact(eventNode, documentUri, facts);
    }

    private static void CollectDialogRefFact(HtmlNode eventNode, string documentUri, List<XmlFact> facts)
    {
        var dialogNode = FindChild(eventNode, "Story_Dialog");
        var dialogName = dialogNode?.InnerText.Trim();
        if (string.IsNullOrEmpty(dialogName)) return;

        // A non-numeric Story_Chapter is already flagged by the Int tag validation; the
        // cross-check only cares about chapters it can actually look up.
        var chapterNode = FindChild(eventNode, "Story_Chapter");
        int? chapter = null;
        var chapterLine = -1;
        if (chapterNode is not null && int.TryParse(chapterNode.InnerText.Trim(), out var parsed))
        {
            chapter = parsed;
            chapterLine = Math.Max(0, chapterNode.Line - 1);
        }

        facts.Add(new StoryDialogRefFact(documentUri, Math.Max(0, dialogNode!.Line - 1), 0, 0,
            dialogName, chapter, chapterLine));
    }

    private static void CollectParamFacts(
        HtmlNode eventNode,
        int eventNodeLine,
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
            var childLine = Math.Max(0, child.Line - 1);

            if (schemaPos > maxDefinedPos)
            {
                facts.Add(new StoryParamFact(documentUri, childLine, 0, 0,
                    eventType, isReward, schemaPos, null, value));
            }
            else
            {
                var paramDef = paramDefs.FirstOrDefault(p => p.Position == schemaPos);
                if (paramDef is null) continue;
                facts.Add(new StoryParamFact(documentUri, childLine, 0, 0,
                    eventType, isReward, schemaPos, paramDef, value));
            }
        }

        foreach (var p in paramDefs.Where(pd => !pd.Optional))
        {
            var child = FindChild(eventNode, $"{prefix}{p.Position + 1}");
            if (child is not null && child.InnerText.Trim().Length > 0) continue;
            facts.Add(new StoryParamFact(documentUri, eventNodeLine, 0, 0,
                eventType, isReward, p.Position, p, ""));
        }
    }

    private static HtmlNode? FindChild(HtmlNode parent, string tagName)
    {
        return parent.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            string.Equals(n.Name, tagName, StringComparison.OrdinalIgnoreCase));
    }
}