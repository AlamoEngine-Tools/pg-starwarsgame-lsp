// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Story.Writer;

/// <summary>
///     Minimal edits over plot manifests (<c>Story_Mode_Plots</c>: Active_Plot / Suspended_Plot /
///     Lua_Script entries) and campaign set files (<c>&lt;Campaign&gt;</c> faction story names).
///     Same contract as <see cref="StoryXmlWriter" />: untouched lines stay byte-identical.
///     Parses fresh per call — manifests are tiny.
/// </summary>
public static class StoryManifestWriter
{
    /// <summary>The skeleton for a newly created story thread file.</summary>
    public const string ThreadFileSkeleton = "<?xml version=\"1.0\" ?>\n<Story>\n</Story>\n";

    /// <summary>Appends an entry (e.g. <c>Active_Plot</c>, <c>Lua_Script</c>) before the root close tag.</summary>
    public static IReadOnlyList<StoryTextEdit> AddEntry(string text, string tagName, string value)
    {
        var document = ParsedXmlDocument.Parse(text);
        var eol = DetectEol(text);
        var entries = Entries(document, null);
        var root = RootElement(document);
        if (root is null) return [];

        string indent;
        int insertLine;
        if (entries.Count > 0)
        {
            var last = entries[^1];
            insertLine = LineOf(document, last.StreamPosition);
            indent = LeadingWhitespace(text, insertLine);
            insertLine += CountLines(last.OuterHtml);
        }
        else
        {
            var rootLine = LineOf(document, root.StreamPosition);
            indent = LeadingWhitespace(text, rootLine) + "\t";
            insertLine = rootLine;
        }

        return [Insert(insertLine + 1, 0, indent + "<" + tagName + ">" + value + "</" + tagName + ">" + eol)];
    }

    /// <summary>Removes the entry line whose tag is one of <paramref name="tagNames" /> and whose value matches.</summary>
    public static IReadOnlyList<StoryTextEdit> RemoveEntry(
        string text, IReadOnlyList<string> tagNames, string value)
    {
        var document = ParsedXmlDocument.Parse(text);
        var entry = FindEntry(document, tagNames, value);
        if (entry is null) return [];

        var line = LineOf(document, entry.StreamPosition);
        return [new StoryTextEdit(new StorySourceRange(line, 0, line + CountLines(entry.OuterHtml) + 1, 0), "")];
    }

    /// <summary>Re-tags an entry in place (Active_Plot ⇄ Suspended_Plot), keeping its value and line.</summary>
    public static IReadOnlyList<StoryTextEdit> RetagEntry(
        string text, IReadOnlyList<string> fromTags, string value, string toTag)
    {
        var document = ParsedXmlDocument.Parse(text);
        var entry = FindEntry(document, fromTags, value);
        if (entry is null) return [];
        if (string.Equals(entry.Name, toTag, StringComparison.OrdinalIgnoreCase)) return [];

        var line = LineOf(document, entry.StreamPosition);
        var indent = LeadingWhitespace(text, line);
        var inner = entry.InnerText.Trim();
        var lineCount = CountLines(entry.OuterHtml);
        return [new StoryTextEdit(new StorySourceRange(line, 0, line + lineCount + 1, 0),
            indent + "<" + toTag + ">" + inner + "</" + toTag + ">" + DetectEol(text))];
    }

    /// <summary>
    ///     Adds a faction story-name tag (<c>{Faction}_Story_Name</c>) inside the named
    ///     <c>&lt;Campaign&gt;</c> element of a campaign set file.
    /// </summary>
    public static IReadOnlyList<StoryTextEdit> AddCampaignStoryName(
        string text, string campaignName, string faction, string manifestFile)
    {
        var document = ParsedXmlDocument.Parse(text);
        var campaign = FindCampaign(document, campaignName);
        if (campaign is null) return [];

        // Anchor after the last existing *_Story_Name (grouped where the engine reads them),
        // falling back to right below the campaign open tag.
        var storyNames = campaign.ChildNodes
            .Where(n => n.NodeType == HtmlNodeType.Element &&
                        n.Name.EndsWith("_Story_Name", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var campaignLine = LineOf(document, campaign.StreamPosition);
        int anchorLine;
        string indent;
        if (storyNames.Count > 0)
        {
            anchorLine = LineOf(document, storyNames[^1].StreamPosition);
            indent = LeadingWhitespace(text, anchorLine);
            anchorLine += CountLines(storyNames[^1].OuterHtml);
        }
        else
        {
            anchorLine = campaignLine;
            indent = LeadingWhitespace(text, campaignLine) + "\t";
        }

        return [Insert(anchorLine + 1, 0,
            indent + "<" + faction + "_Story_Name>" + manifestFile + "</" + faction + "_Story_Name>" +
            DetectEol(text))];
    }

    /// <summary>Removes a faction story-name entry from the named campaign.</summary>
    public static IReadOnlyList<StoryTextEdit> RemoveCampaignStoryName(
        string text, string campaignName, string manifestFile)
    {
        var document = ParsedXmlDocument.Parse(text);
        var campaign = FindCampaign(document, campaignName);
        var entry = campaign?.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.EndsWith("_Story_Name", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.InnerText.Trim(), manifestFile, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return [];

        var line = LineOf(document, entry.StreamPosition);
        return [new StoryTextEdit(new StorySourceRange(line, 0, line + CountLines(entry.OuterHtml) + 1, 0), "")];
    }

    // ── Mechanics ────────────────────────────────────────────────────────────

    private static HtmlNode? FindEntry(ParsedXmlDocument document, IReadOnlyList<string> tagNames, string value)
    {
        return Entries(document, tagNames).FirstOrDefault(n =>
            string.Equals(n.InnerText.Trim(), value, StringComparison.OrdinalIgnoreCase));
    }

    private static List<HtmlNode> Entries(ParsedXmlDocument document, IReadOnlyList<string>? tagNames)
    {
        var root = RootElement(document);
        if (root is null) return [];
        return root.ChildNodes
            .Where(n => n.NodeType == HtmlNodeType.Element &&
                        (tagNames is null || tagNames.Any(t =>
                            string.Equals(t, n.Name, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    private static HtmlNode? FindCampaign(ParsedXmlDocument document, string campaignName)
    {
        return document.Html.DocumentNode.Descendants().FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.Equals("Campaign", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.GetAttributeValue("Name", "").Trim(), campaignName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static HtmlNode? RootElement(ParsedXmlDocument document)
    {
        return document.Html.DocumentNode.ChildNodes
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
    }

    private static int LineOf(ParsedXmlDocument document, int offset)
    {
        var (line, _) = document.LineIndex.GetPosition(offset);
        return line;
    }

    private static string LeadingWhitespace(string text, int line)
    {
        var offset = 0;
        for (var i = 0; i < line; i++)
        {
            var next = text.IndexOf('\n', offset);
            if (next < 0) return "";
            offset = next + 1;
        }

        var length = 0;
        while (offset + length < text.Length && (text[offset + length] == ' ' || text[offset + length] == '\t'))
            length++;
        return text.Substring(offset, length);
    }

    private static int CountLines(string outerHtml)
    {
        return outerHtml.Count(c => c == '\n');
    }

    private static StoryTextEdit Insert(int line, int column, string newText)
    {
        return new StoryTextEdit(new StorySourceRange(line, column, line, column), newText);
    }

    private static string DetectEol(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }
}
