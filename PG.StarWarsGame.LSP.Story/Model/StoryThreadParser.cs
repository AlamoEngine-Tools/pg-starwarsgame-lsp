// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Story.Model;

/// <summary>
///     Parses a story thread file (<c>&lt;Event&gt;</c> blocks) into the from-scratch story
///     domain model. All XML reading goes through HAP/<see cref="ParsedXmlDocument" /> (repo
///     rule); source ranges are computed from HAP stream positions against the original text so
///     they are column-accurate.
/// </summary>
public static class StoryThreadParser
{
    public static StoryThread Parse(string text, string documentUri)
    {
        return Parse(ParsedXmlDocument.Parse(text), documentUri);
    }

    public static StoryThread Parse(ParsedXmlDocument document, string documentUri)
    {
        var events = new List<StoryEvent>();
        var problems = new List<StoryParseProblem>();

        foreach (var node in document.Html.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 n.Name.Equals("Event", StringComparison.OrdinalIgnoreCase)))
        {
            var nameAttribute = node.Attributes["Name"];
            var name = nameAttribute?.Value.Trim();
            if (string.IsNullOrEmpty(name))
            {
                problems.Add(new StoryParseProblem(RangeAt(document, node.StreamPosition, "<Event".Length),
                    "Event without a Name attribute — the block is not addressable and was skipped."));
                continue;
            }

            events.Add(ParseEvent(document, node, name, nameAttribute!));
        }

        return new StoryThread(documentUri, events, problems);
    }

    private static StoryEvent ParseEvent(
        ParsedXmlDocument document, HtmlNode node, string name, HtmlAttribute nameAttribute)
    {
        var tags = new List<StoryEventTag>();
        var eventParams = new List<StoryParamSlot>();
        var rewardParams = new List<StoryParamSlot>();
        var prereqGroups = new List<StoryPrereqGroup>();
        string? eventType = null, eventFilter = null, rewardType = null, branch = null, storyDialog = null;
        var perpetual = false;
        int? storyChapter = null;

        foreach (var child in node.ChildNodes.Where(c => c.NodeType == HtmlNodeType.Element))
        {
            var tagName = XmlUtility.GetOriginalTagName(child, document.Text);
            var (value, valueRange) = InnerValue(document, child);
            tags.Add(new StoryEventTag(tagName, value, valueRange));
            if (value.Length == 0 && !tagName.Equals("Prereq", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (tagName.ToUpperInvariant())
            {
                case "EVENT_TYPE":
                    eventType = value;
                    break;
                case "EVENT_FILTER":
                    eventFilter = value;
                    break;
                case "REWARD_TYPE":
                    rewardType = value;
                    break;
                case "BRANCH":
                    branch = value;
                    break;
                case "PERPETUAL":
                    perpetual = value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                                value == "1";
                    break;
                case "STORY_DIALOG":
                    storyDialog = value;
                    break;
                case "STORY_CHAPTER" when int.TryParse(value, out var chapter):
                    storyChapter = chapter;
                    break;
                case "PREREQ":
                    prereqGroups.Add(ParsePrereqGroup(document, child, valueRange));
                    break;
                default:
                    if (TryParamPosition(tagName, "Event_Param", out var eventPos))
                        eventParams.Add(new StoryParamSlot(eventPos, value, valueRange));
                    else if (TryParamPosition(tagName, "Reward_Param", out var rewardPos))
                        rewardParams.Add(new StoryParamSlot(rewardPos, value, valueRange));
                    break;
            }
        }

        return new StoryEvent
        {
            Name = name,
            NameRange = RangeAt(document, nameAttribute.ValueStartIndex, nameAttribute.Value.Length),
            Range = RangeAt(document, node.StreamPosition, node.OuterHtml.Length),
            EventType = eventType,
            EventFilter = eventFilter,
            EventParams = eventParams,
            RewardType = rewardType,
            RewardParams = rewardParams,
            PrereqGroups = prereqGroups,
            Branch = branch,
            Perpetual = perpetual,
            StoryDialog = storyDialog,
            StoryChapter = storyChapter,
            Tags = tags
        };
    }

    // "Event_Param3" → position 2. "Reward_Param_List" deliberately fails the numeric parse and
    // stays a plain tag — it is not a positional slot.
    private static bool TryParamPosition(string tagName, string prefix, out int position)
    {
        position = -1;
        if (!tagName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(tagName.AsSpan(prefix.Length), out var slot) || slot < 1) return false;
        position = slot - 1;
        return true;
    }

    private static StoryPrereqGroup ParsePrereqGroup(
        ParsedXmlDocument document, HtmlNode child, StorySourceRange groupRange)
    {
        var tokens = new List<StoryToken>();

        // Token spans are only exact when the inner content is plain text; mixed content
        // (comments inside a Prereq line — unseen in vanilla) degrades to the group range.
        if (child.ChildNodes.All(c => c.NodeType == HtmlNodeType.Text))
        {
            var innerStart = child.InnerStartIndex;
            var raw = document.Text.Substring(innerStart, child.InnerLength);
            var i = 0;
            while (i < raw.Length)
            {
                if (char.IsWhiteSpace(raw[i]))
                {
                    i++;
                    continue;
                }

                var start = i;
                while (i < raw.Length && !char.IsWhiteSpace(raw[i])) i++;
                tokens.Add(new StoryToken(raw[start..i], RangeAt(document, innerStart + start, i - start)));
            }
        }
        else
        {
            foreach (var token in child.InnerText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                tokens.Add(new StoryToken(token, groupRange));
        }

        return new StoryPrereqGroup(tokens, groupRange);
    }

    private static (string Value, StorySourceRange Range) InnerValue(ParsedXmlDocument document, HtmlNode child)
    {
        var trimmed = child.InnerText.Trim();
        if (child.InnerLength <= 0 || trimmed.Length == 0)
            return (trimmed, RangeAt(document, Math.Max(child.InnerStartIndex, 0), 0));

        var raw = document.Text.Substring(child.InnerStartIndex, child.InnerLength);
        var idx = raw.IndexOf(trimmed, StringComparison.Ordinal);
        return idx >= 0
            ? (trimmed, RangeAt(document, child.InnerStartIndex + idx, trimmed.Length))
            : (trimmed, RangeAt(document, child.InnerStartIndex, child.InnerLength));
    }

    private static StorySourceRange RangeAt(ParsedXmlDocument document, int offset, int length)
    {
        var (startLine, startColumn) = document.LineIndex.GetPosition(offset);
        var (endLine, endColumn) = document.LineIndex.GetPosition(offset + length);
        return new StorySourceRange(startLine, startColumn, endLine, endColumn);
    }
}
