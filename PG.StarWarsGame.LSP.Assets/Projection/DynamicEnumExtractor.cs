// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Extracts dynamic enum values from game data files and populates
///     <see cref="Core.Symbols.BaselineIndex.DynamicEnumValues" /> and
///     <see cref="Core.Symbols.BaselineIndex.HardcodedEnumValues" />.
/// </summary>
/// <remarks>
///     Two source formats are supported, encoded in <see cref="EnumDefinition.SourceFile" />:
///     <list type="bullet">
///         <item>
///             <c>path$Element</c> (e.g. <c>data/xml/gameconstants.xml$Damage_Types</c>) — text content of a
///             named element inside the file; supports the "ABOVE this point" boundary comment that separates
///             mod-extensible values from engine-hardcoded ones.
///         </item>
///         <item>
///             <c>path</c> only (e.g. <c>data/xml/enum/aigoalcategorytype.xml</c>) — an
///             <c>&lt;EnumDefinition&gt;</c> document whose child element names are the enum values.
///         </item>
///     </list>
/// </remarks>
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

        // Cache parsed XDocuments so files read by multiple enums are only loaded once.
        var docCache = new Dictionary<string, XDocument?>(StringComparer.OrdinalIgnoreCase);

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
                    var content = fileReader(filePath);
                    doc = TryParse(content);
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

        XDocument doc;
        try { doc = XDocument.Parse(xml, LoadOptions.SetLineInfo); }
        catch (XmlException) { return []; }

        var el = doc.Descendants(elementName).FirstOrDefault();
        if (el is null) return [];

        var result = new List<(string Name, FileOrigin Origin)>();
        foreach (var node in el.Nodes())
        {
            if (node is XComment comment && IsBoundaryComment(comment.Value))
                break;

            if (node is not XText textNode) continue;

            var text = textNode.Value;
            var li = (IXmlLineInfo)textNode;
            var startLine = li.HasLineInfo() ? li.LineNumber - 1 : 0;
            var startCol = li.HasLineInfo() ? li.LinePosition - 1 : 0;

            foreach (var (token, offset) in SplitTokensWithOffsets(text))
            {
                var (line, col) = AdvancePosition(text, offset, startLine, startCol);
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

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return [];
        }

        var root = doc.Root;
        if (root is null) return [];

        var result = new List<(string Name, FileOrigin Origin)>();
        foreach (var element in root.Elements())
        {
            var line = element is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber - 1 : 0;
            result.Add((element.Name.LocalName, new FileOrigin(uri, line, null)));
        }

        return result;
    }

    // Walk `offset` characters through `text`, starting at (startLine, startCol),
    // tracking newlines to compute the final 0-based (line, col).
    private static (int Line, int Col) AdvancePosition(string text, int offset, int startLine, int startCol)
    {
        int line = startLine, col = startCol;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') { line++; col = 0; }
            else if (text[i] != '\r') { col++; }
        }

        return (line, col);
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

    private static bool IsTokenSeparator(char c) => c is ' ' or '\t' or '\r' or '\n' or ',';

    /// <summary>
    ///     Parses an <c>&lt;EnumDefinition&gt;</c> XML document and returns the child element names as enum values.
    ///     Text content and numeric weights inside each element are ignored.
    /// </summary>
    internal static ImmutableArray<string> ParseEnumDefinitionFile(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return [];

        var doc = TryParse(xml);
        if (doc is null) return [];

        var root = doc.Root;
        if (root is null) return [];

        return [..root.Elements().Select(e => e.Name.LocalName)];
    }

    internal static (ImmutableArray<string> all, ImmutableArray<string> hardcoded)
        ParseNameListWithBoundary(XDocument doc, string tagName)
    {
        var el = doc.Descendants(tagName).FirstOrDefault();
        if (el is null) return ([], []);

        var all = new List<string>();
        var hardcoded = new List<string>();
        var pastBoundary = false;

        foreach (var node in el.Nodes())
        {
            if (node is XComment comment && IsBoundaryComment(comment.Value))
            {
                pastBoundary = true;
                continue;
            }

            IEnumerable<string> tokens;
            if (node is XText text)
            {
                tokens = text.Value.Split((char[])[' ', '\t', '\r', '\n', ','],
                    StringSplitOptions.RemoveEmptyEntries);
            }
            else if (node is XElement child)
            {
                var v = child.Value.Trim();
                tokens = v.Length > 0 ? [v] : [];
            }
            else
            {
                continue;
            }

            foreach (var t in tokens)
            {
                all.Add(t);
                if (pastBoundary) hardcoded.Add(t);
            }
        }

        return ([..all], [..hardcoded]);
    }

    internal static bool IsBoundaryComment(string commentText)
    {
        return commentText.Contains("ABOVE this point", StringComparison.OrdinalIgnoreCase);
    }

    private static XDocument? TryParse(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        try
        {
            return XDocument.Parse(xml);
        }
        catch
        {
            return null;
        }
    }
}