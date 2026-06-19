// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.Completion;

internal sealed class StoryEventTagNameStrategy : IXmlTagNameCompletionStrategy
{
    private const int MaxEventParamSlots = 7;
    private const int MaxRewardParamSlots = 14;

    private static readonly string[] StructuralTags =
    [
        "Event_Type", "Event_Filter", "Reward_Type", "Reward_Position",
        "Prereq", "Branch", "Perpetual", "Multiplayer",
        "Story_Dialog", "Story_Chapter", "Story_Tag", "Story_Var",
        "Story_Dialog_Popup", "Story_Dialog_SFX",
        "Inactive_Delay", "Timeout"
    ];

    public IEnumerable<CompletionItem> Handle(TagNameCompletionContext ctx)
    {
        if (!ctx.IsStoryParser) return [];
        if (!string.Equals(ctx.EnclosingTag, "Event", StringComparison.OrdinalIgnoreCase)) return [];

        var doc = new HtmlDocument();
        doc.LoadHtml(ctx.Text);

        var storyCtx = StoryEventCompletionContextReader.Read(doc, ctx.LineIndex, ctx.Character);
        var eventDef = storyCtx.EventType is not null
            ? ctx.Schema.GetEnum("StoryEventType")?.Values
                .FirstOrDefault(v => string.Equals(v.Name, storyCtx.EventType, StringComparison.OrdinalIgnoreCase))
            : null;
        var rewardDef = storyCtx.RewardType is not null
            ? ctx.Schema.GetEnum("StoryRewardType")?.Values
                .FirstOrDefault(v => string.Equals(v.Name, storyCtx.RewardType, StringComparison.OrdinalIgnoreCase))
            : null;

        var candidates = new List<string>(StructuralTags);

        if (eventDef is not null)
        {
            var paramCount = eventDef.Params is null
                ? MaxEventParamSlots
                : eventDef.Params.Count > 0
                    ? eventDef.Params.Max(p => p.Position) + 1
                    : 0;
            for (var i = 1; i <= paramCount; i++)
                candidates.Add($"Event_Param{i}");
        }

        if (rewardDef is not null)
        {
            var paramCount = rewardDef.Params is null
                ? MaxRewardParamSlots
                : rewardDef.Params.Count > 0
                    ? rewardDef.Params.Max(p => p.Position) + 1
                    : 0;
            for (var i = 1; i <= paramCount; i++)
                candidates.Add($"Reward_Param{i}");
        }

        var existing = CollectExistingEventChildTagNames(doc, ctx.LineIndex);

        return candidates
            .Where(t => !existing.Contains(t))
            .Where(t => ctx.Prefix.Length == 0 || t.StartsWith(ctx.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(t => new CompletionItem
            {
                Label = t,
                Kind = CompletionItemKind.Property,
                InsertText = $"{t}>$0</{t}>",
                InsertTextFormat = InsertTextFormat.Snippet
            });
    }

    private static HashSet<string> CollectExistingEventChildTagNames(HtmlDocument doc, int lineIndex)
    {
        var cursorLine = lineIndex + 1;
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        HtmlNode? enclosingEvent = null;
        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 string.Equals(n.Name, "event", StringComparison.OrdinalIgnoreCase)))
            if (node.Line <= cursorLine)
                enclosingEvent = node;
            else
                break;

        if (enclosingEvent is null) return result;
        foreach (var child in enclosingEvent.ChildNodes)
            if (child.NodeType == HtmlNodeType.Element)
                result.Add(child.Name);
        return result;
    }
}