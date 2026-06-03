// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Xml.Completion;

public static class StoryEventCompletionContextReader
{
    public static StoryEventCompletionContext Read(HtmlDocument doc, int line, int character)
    {
        var cursorLine = line + 1; // HAP uses 1-based line numbers

        HtmlNode? enclosingEvent = null;
        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 string.Equals(n.Name, "event", StringComparison.OrdinalIgnoreCase)))
            if (node.Line <= cursorLine)
                enclosingEvent = node;
            else
                break;

        if (enclosingEvent is null)
            return new StoryEventCompletionContext(null, null);

        var eventType = GetChildText(enclosingEvent, "event_type");
        var rewardType = GetChildText(enclosingEvent, "reward_type");
        return new StoryEventCompletionContext(
            string.IsNullOrEmpty(eventType) ? null : eventType,
            string.IsNullOrEmpty(rewardType) ? null : rewardType);
    }

    private static string? GetChildText(HtmlNode parent, string tagName)
    {
        var child = parent.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            string.Equals(n.Name, tagName, StringComparison.OrdinalIgnoreCase));
        var text = child?.InnerText.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}