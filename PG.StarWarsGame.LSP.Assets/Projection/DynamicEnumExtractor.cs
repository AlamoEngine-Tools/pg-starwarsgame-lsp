// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Reads the values of the schema's dynamic enums out of the game's own files. Parsed through
///     <see cref="XmlUtility" /> like every other game XML (#308): the game's reader matches an
///     element name in any case and takes a file a strict parser rejects, and so does this.
/// </summary>
public static class DynamicEnumExtractor
{
    /// <summary>
    ///     Extracts dynamic enum values for every <see cref="EnumKind.DynamicXml" /> enum in the schema.
    /// </summary>
    /// <param name="schema">Schema that declares which enums to extract and from which files.</param>
    /// <param name="fileReader">
    ///     Callback that returns the XML text for an archive-root-relative file path (lowercase,
    ///     forward-slash). Returns <see langword="null" /> if the file is unavailable.
    /// </param>
    public static (
        ImmutableDictionary<string, ImmutableArray<string>> dynamic,
        ImmutableDictionary<string, ImmutableArray<string>> hardcoded
        ) Extract(ISchemaProvider schema, Func<string, string?> fileReader)
    {
        var dyn = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>();
        var hard = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>();

        // Cache parsed documents so files read by multiple enums are only loaded once.
        var docCache = new Dictionary<string, HtmlDocument?>(StringComparer.OrdinalIgnoreCase);

        foreach (var enumDef in schema.AllEnums)
        {
            if (enumDef.Kind != EnumKind.DynamicXml || string.IsNullOrEmpty(enumDef.SourceFile))
                continue;

            var sourceFile = enumDef.SourceFile;
            var anchorIdx = sourceFile.IndexOf('$');

            if (anchorIdx >= 0)
            {
                // path$Element format: text-list inside a named element of an XML file.
                var filePath = sourceFile[..anchorIdx];
                var elementName = sourceFile[(anchorIdx + 1)..];

                if (!docCache.TryGetValue(filePath, out var doc))
                {
                    doc = TryParse(fileReader(filePath));
                    docCache[filePath] = doc;
                }

                if (doc is null) continue;

                var (all, hardcoded) = ParseNameListWithBoundary(doc, elementName);
                if (all.Length > 0) dyn[enumDef.Name] = all;
                if (hardcoded.Length > 0) hard[enumDef.Name] = hardcoded;
            }
            else
            {
                // Plain-path format: <EnumDefinition> file where child element names are values.
                var content = fileReader(sourceFile);
                var values = ParseEnumDefinitionFile(content);
                if (values.Length > 0) dyn[enumDef.Name] = values;
                // No hardcoded-boundary concept for this format.
            }
        }

        return (dyn.ToImmutable(), hard.ToImmutable());
    }

    /// <summary>
    ///     Finds a named child element inside <paramref name="xml" /> and returns each whitespace/comma-
    ///     separated token in its text content with an exact 0-based (line, column) location.
    ///     Stops at the first "ABOVE this point" boundary comment so hardcoded-only tokens are excluded.
    /// </summary>
    public static IReadOnlyList<(string Name, FileOrigin Origin)> ParseElementTextWithLocations(
        string? xml, string elementName, string uri)
    {
        if (string.IsNullOrEmpty(xml)) return [];

        var el = FindElement(XmlUtility.CreateHtmlDocument(xml), elementName);
        if (el is null) return [];

        var lineIndex = new LineOffsetIndex(xml);
        var result = new List<(string Name, FileOrigin Origin)>();
        foreach (var node in el.ChildNodes)
        {
            if (node is HtmlCommentNode comment && IsBoundaryComment(comment.Comment))
                break;

            if (node.NodeType != HtmlNodeType.Text) continue;

            foreach (var (token, offset) in SplitTokensWithOffsets(node.InnerText))
            {
                var (line, col) = lineIndex.GetPosition(node.StreamPosition + offset);
                result.Add((token, new FileOrigin(uri, line, col)));
            }
        }

        return result;
    }

    /// <summary>
    ///     Parses an <c>&lt;EnumDefinition&gt;</c> XML document and returns each child element's name paired
    ///     with its 0-based line number in <paramref name="uri" />.
    /// </summary>
    public static IReadOnlyList<(string Name, FileOrigin Origin)> ParseEnumDefinitionFileWithLocations(
        string? xml, string uri)
    {
        if (string.IsNullOrEmpty(xml)) return [];
        if (!XmlUtility.TryGetRootNode(XmlUtility.CreateHtmlDocument(xml), out var root) || root is null) return [];

        var result = new List<(string Name, FileOrigin Origin)>();
        foreach (var element in root.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            result.Add((XmlUtility.GetOriginalTagName(element, xml),
                new FileOrigin(uri, Math.Max(0, XmlUtility.GetLine(element)), null)));

        return result;
    }

    private static IEnumerable<(string Token, int Offset)> SplitTokensWithOffsets(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && IsTokenSeparator(text[i])) i++;
            if (i >= text.Length) break;
            var start = i;
            while (i < text.Length && !IsTokenSeparator(text[i])) i++;
            yield return (text[start..i], start);
        }
    }

    private static bool IsTokenSeparator(char c)
    {
        return c is ' ' or '\t' or '\r' or '\n' or ',';
    }

    /// <summary>
    ///     Parses an <c>&lt;EnumDefinition&gt;</c> XML document and returns the child element names as enum
    ///     values, spelled as the file spells them. Text content and numeric weights inside each
    ///     element are ignored.
    /// </summary>
    internal static ImmutableArray<string> ParseEnumDefinitionFile(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return [];
        if (!XmlUtility.TryGetRootNode(XmlUtility.CreateHtmlDocument(xml), out var root) || root is null) return [];

        return
        [
            .. root.ChildNodes
                .Where(n => n.NodeType == HtmlNodeType.Element)
                .Select(e => XmlUtility.GetOriginalTagName(e, xml))
        ];
    }

    internal static (ImmutableArray<string> all, ImmutableArray<string> hardcoded)
        ParseNameListWithBoundary(HtmlDocument doc, string tagName)
    {
        var el = FindElement(doc, tagName);
        if (el is null) return ([], []);

        var all = new List<string>();
        var hardcoded = new List<string>();
        var pastBoundary = false;

        foreach (var node in el.ChildNodes)
        {
            if (node is HtmlCommentNode comment && IsBoundaryComment(comment.Comment))
            {
                pastBoundary = true;
                continue;
            }

            IEnumerable<string> tokens;
            switch (node.NodeType)
            {
                case HtmlNodeType.Text:
                    tokens = node.InnerText.Split((char[])[' ', '\t', '\r', '\n', ','],
                        StringSplitOptions.RemoveEmptyEntries);
                    break;
                case HtmlNodeType.Element:
                    var v = node.InnerText.Trim();
                    tokens = v.Length > 0 ? [v] : [];
                    break;
                default:
                    continue;
            }

            foreach (var t in tokens)
            {
                all.Add(t);
                if (pastBoundary) hardcoded.Add(t);
            }
        }

        return ([.. all], [.. hardcoded]);
    }

    internal static bool IsBoundaryComment(string commentText)
    {
        return commentText.Contains("ABOVE this point", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The first element of that name, in any case - the game's reader does not distinguish.</summary>
    private static HtmlNode? FindElement(HtmlDocument doc, string elementName)
    {
        return doc.DocumentNode.Descendants().FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element && n.Name.Equals(elementName, StringComparison.OrdinalIgnoreCase));
    }

    private static HtmlDocument? TryParse(string? xml)
    {
        return string.IsNullOrEmpty(xml) ? null : XmlUtility.CreateHtmlDocument(xml);
    }
}