// // Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// // Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Util;

public static class XmlUtility
{
    public const int InvalidLineMarker = -1;

    public static HtmlDocument CreateHtmlDocument(string xmlContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(xmlContent);
        return doc;
    }

    public static int GetLine(HtmlNode? node)
    {
        if (node is null) return InvalidLineMarker;
        return node.Line - 1;
    }

    public static int GetPrintableLine(HtmlNode? node)
    {
        if (node is null) return InvalidLineMarker;
        return node.Line;
    }

    public static int GetOpeningTagStartColumn(HtmlNode? node)
    {
        if (node is null) return InvalidLineMarker;
        return node.LinePosition + 1;
    }

    public static int GetOpeningTagEndColumn(HtmlNode? node)
    {
        if (node is null) return InvalidLineMarker;
        return GetOpeningTagStartColumn(node) + node.Name.Length;
    }

    /// <summary>
    ///     Value of an object's identifier attribute, trimmed, or null when absent or blank.
    ///     HtmlAgilityPack lowercases attribute names, so the match is case-insensitive.
    /// </summary>
    /// <param name="node">The object element.</param>
    /// <param name="nameTag">
    ///     The identifier attribute, from the object type's <c>nameTag</c>. Defaults to <c>Name</c>,
    ///     which every currently registered type uses - pass the schema's value where it is known
    ///     rather than relying on that.
    /// </param>
    public static string? GetNameAttributeValue(HtmlNode node, string nameTag = "Name")
    {
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals(nameTag, StringComparison.OrdinalIgnoreCase));
        var value = attr?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    ///     Recovers the original (case-preserving) tag name for <paramref name="node" /> from the
    ///     source <paramref name="text" />. HtmlAgilityPack lowercases <see cref="HtmlNode.Name" />,
    ///     which is wrong for user-facing messages on the case-sensitive EaW/FoC XML format.
    /// </summary>
    public static string GetOriginalTagName(HtmlNode node, string text)
    {
        var start = node.StreamPosition + 1; // skip '<'
        if (start <= 0 || start >= text.Length) return node.Name;
        var i = start;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '/')
            i++;
        return i > start ? text[start..i] : node.Name;
    }

    public static bool TryGetRootNode(HtmlDocument doc, out HtmlNode? rootNode)
    {
        rootNode = doc.DocumentNode
            .ChildNodes
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
        return rootNode is not null;
    }

    public static bool TryFindNode(HtmlDocument doc, int line, out HtmlNode? node)
    {
        node = doc.DocumentNode
            .Descendants()
            .FirstOrDefault(n =>
                n.NodeType == HtmlNodeType.Element &&
                n.Line - 1 == line);
        return node is not null;
    }

    public static bool TryFindNode(HtmlDocument doc, string tagName, int line, out HtmlNode? node)
    {
        node = doc.DocumentNode
            .Descendants()
            .FirstOrDefault(n =>
                n.NodeType == HtmlNodeType.Element &&
                n.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase) &&
                n.Line - 1 == line);
        return node is not null;
    }

    public static int GetDepth(HtmlNode? node)
    {
        if (node is null) return InvalidLineMarker;
        var depth = 0;
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        // ParentNode may be null according to HAP documentation. This is the case for the document root node.
        while (node.ParentNode != null)
        {
            depth++;
            node = node.ParentNode;
        }

        return depth;
    }

    public static List<string> SplitList(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<string>();

        // 1. Remove XML comments
        input = Regex.Replace(input, "<!--.*?-->", "", RegexOptions.Singleline);

        // 2. Normalize whitespace
        input = Regex.Replace(input, @"\s+", " ");

        // 3. Normalize separators (space is also a list separator in EaW XML)
        string[] seps = { ",", ";", "|", "/", "\\", " " };
        foreach (var sep in seps)
            input = input.Replace(sep, ",");

        // 4. Split into values
        return input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public static IReadOnlyList<(string Token, int Offset)> SplitListWithOffsets(string input)
    {
        var tokens = SplitList(input);
        var results = new List<(string, int)>(tokens.Count);
        var searchFrom = 0;
        foreach (var token in tokens)
        {
            var idx = input.IndexOf(token, searchFrom, StringComparison.Ordinal);
            if (idx >= 0) searchFrom = idx + token.Length;
            results.Add((token, idx >= 0 ? idx : 0));
        }

        return results;
    }

    public static string? GetXmlObjectId(GameObjectTypeDefinition typeDef, HtmlNode node)
    {
        return node.GetAttributes().FirstOrDefault(a =>
            string.Equals(a.Name, typeDef.NameTag, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    public static bool IsOnTagName(HtmlNode node, int line, int character)
    {
        if (GetLine(node) == line)
        {
            var openStart = GetOpeningTagStartColumn(node);
            if (character >= openStart && character < openStart + node.Name.Length)
                return true;
        }

        var endNode = node.EndNode;
        if (endNode != null && !ReferenceEquals(endNode, node))
        {
            var closeNameStart = endNode.LinePosition + 2; // skip </
            if (GetLine(endNode) == line && character >= closeNameStart &&
                character < closeNameStart + node.Name.Length)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Converts a snake_case or Snake_Case element name (as the game or HtmlAgilityPack produces it)
    ///     to the PascalCase schema type name used in the YAML tag files.
    ///     Example: "lucky_shot_attack_ability" → "LuckyShotAttackAbility"
    /// </summary>
    public static string ToPascalCase(string snakeName)
    {
        return string.Concat(snakeName.Split('_')
            .Select(w => w.Length == 0 ? "" : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>
    ///     Converts a PascalCase schema type name to the Snake_Case element name used in game XML.
    ///     Example: "LuckyShotAttackAbility" → "Lucky_Shot_Attack_Ability"
    /// </summary>
    public static string ToSnakeCase(string pascalName)
    {
        if (pascalName.Length == 0) return pascalName;
        return string.Join("_", Regex.Split(pascalName, @"(?<=[a-z])(?=[A-Z])"));
    }

    public static bool TryFindNodeByClosingLine(HtmlDocument doc, int line, out HtmlNode? node)
    {
        node = doc.DocumentNode
            .Descendants()
            .FirstOrDefault(n =>
                n.NodeType == HtmlNodeType.Element &&
                n.EndNode != null &&
                !ReferenceEquals(n.EndNode, n) &&
                GetLine(n.EndNode) == line);
        return node is not null;
    }

    /// <summary>
    ///     Returns the deepest element that structurally contains <paramref name="cursorLine" /> (0-based).
    ///     Elements with no explicit closing tag (auto-closed by HAP) are treated as extending to
    ///     end-of-document - correct for truncated text where the closing tag was cut off.
    /// </summary>
    public static HtmlNode? FindEnclosingElement(HtmlDocument doc, int cursorLine)
    {
        HtmlNode? best = null;
        var bestDepth = -1;
        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var startLine = GetLine(node);
            if (startLine > cursorLine) continue;
            // Use the explicit closing tag when HAP found one at a valid position.
            // For elements without a valid EndNode there are two distinct cases:
            //   • Self-closing (<Tag />) - HAP sets EndNode = the node itself (ReferenceEquals).
            //     These can never enclose anything; bound them to their own start line.
            //   • Null or synthetic EndNode (Line = 0) - the closing tag is absent (truncated
            //     document) or was auto-generated by HAP.  Treat as open to end of document so
            //     the enclosing type is still found while the user is actively typing.
            var endLine = node.EndNode is not null && !ReferenceEquals(node.EndNode, node)
                                                   && node.EndNode.Line >= node.Line
                ? GetLine(node.EndNode) // explicit, valid closing tag
                : ReferenceEquals(node.EndNode, node)
                    ? GetLine(node) // self-closing (<Tag />) - own line only
                    : node.EndNode is null || node.ChildNodes.Any(c => c.NodeType == HtmlNodeType.Element)
                        ? int.MaxValue // null=truncated doc; with children=synthetic close on container
                        : GetLine(node); // synthetic close (EndNode.Line=0), no children - leaf
            if (cursorLine > endLine) continue;

            var depth = GetDepth(node);
            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = node;
            }
        }

        return best;
    }

    /// <summary>
    ///     Returns the LSP 0-based column of the opening <c>&lt;</c> bracket of <paramref name="node" />.
    ///     Use <see cref="GetOpeningTagStartColumn" /> for the column of the tag name (one past the bracket).
    /// </summary>
    public static int GetTagBracketColumn(HtmlNode? node)
    {
        return node is null ? InvalidLineMarker : node.LinePosition;
    }

    /// <summary>
    ///     Returns the byte length of the opening tag including <c>&lt;</c>, name, attributes, and <c>&gt;</c>.
    ///     Uses <c>InnerStartIndex - StreamPosition</c> which HAP computes natively.
    /// </summary>
    public static int GetOpeningTagLength(HtmlNode? node)
    {
        return node is null ? 0 : node.InnerStartIndex - node.StreamPosition;
    }

    /// <summary>
    ///     Converts an absolute byte <paramref name="offset" /> in <paramref name="text" /> to a
    ///     0-based LSP <c>(Line, Col)</c> position by counting newlines.
    /// </summary>
    /// <summary>
    ///     Returns the position immediately after <paramref name="node" />'s closing <c>&gt;</c> —
    ///     the closing tag's for a normal element, or the opening tag's own for a self-closing one.
    ///     Used to build a diagnostic <c>Range</c> that spans the whole element (opening tag through
    ///     closing tag), not just a single line, e.g. greying out an entire redundant-override node.
    /// </summary>
    public static (int Line, int Col) GetElementEndPosition(HtmlNode node, string text)
    {
        var tagNode = node.EndNode is not null && !ReferenceEquals(node.EndNode, node) ? node.EndNode : node;
        var searchStart = Math.Max(0, tagNode.StreamPosition);
        var closeBracket = text.IndexOf('>', searchStart);
        var endOffset = closeBracket >= 0 ? closeBracket + 1 : text.Length;
        return OffsetToPosition(text, endOffset);
    }

    /// <summary>
    ///     Position and length of an element's whole trimmed inner-text value - the "highlight the
    ///     value, not the tag" anchor a diagnostic points at when a leaf value is wrong. The start skips
    ///     whitespace inside the element and the length is the trimmed value's source span. Always
    ///     compute value ranges through this (or <see cref="GetInnerOffsetValuePosition" /> for one item
    ///     of a list value): both use the document <paramref name="lineIndex" /> and the element's
    ///     native <c>InnerStartIndex</c>, not HAP's per-node line/column, which is unreliable for nested
    ///     elements and yields invalid ranges the client silently drops. Returns a zero-length span at
    ///     the end of the inner content when the element has no non-whitespace value.
    /// </summary>
    public static (int Line, int Column, int Length) GetValuePosition(HtmlNode node, LineOffsetIndex lineIndex)
    {
        var innerHtml = node.InnerHtml;
        var leading = innerHtml.Length - innerHtml.TrimStart().Length;
        return GetInnerOffsetValuePosition(node, leading, innerHtml.Trim().Length, lineIndex);
    }

    /// <summary>
    ///     Position of a single value token at <paramref name="innerOffset" /> characters into an
    ///     element's inner content, with the given <paramref name="length" />. The list-valued companion
    ///     to <see cref="GetValuePosition" />: for a space/comma-separated tag, the offset and length of
    ///     each item come from <see cref="SplitListWithOffsets" />, so each item is highlighted
    ///     individually rather than the whole tag. Shares the same <c>InnerStartIndex</c> +
    ///     <paramref name="lineIndex" /> basis, so list-item and whole-value ranges can never drift apart.
    /// </summary>
    public static (int Line, int Column, int Length) GetInnerOffsetValuePosition(
        HtmlNode node, int innerOffset, int length, LineOffsetIndex lineIndex)
    {
        var (line, column) = lineIndex.GetPosition(node.InnerStartIndex + innerOffset);
        return (line, column, length);
    }

    /// <summary>
    ///     Walks <paramref name="text" /> from its own start up to <paramref name="offset" />,
    ///     advancing (<paramref name="startLine" />, <paramref name="startCol" />) by each character
    ///     crossed. Used to locate a sub-token within a fact's <c>RawValue</c> (which may itself span
    ///     multiple lines) without needing the enclosing document's full text - the fact's own
    ///     Line/Column is the starting point.
    /// </summary>
    public static (int Line, int Col) AdvancePosition(int startLine, int startCol, string text, int offset)
    {
        var line = startLine;
        var col = startCol;
        for (var i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n')
            {
                line++;
                col = 0;
            }
            else if (text[i] != '\r')
            {
                col++;
            }

        return (line, col);
    }

    public static (int Line, int Col) OffsetToPosition(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }

        // A negative offset (e.g. an HtmlAgilityPack InnerStartIndex of -1, or an IndexOf miss) would
        // otherwise yield a negative column, which LSP positions forbid - clamp to 0.
        return (line, Math.Max(0, offset - lineStart));
    }

    /// <summary>
    ///     Inverse of <see cref="OffsetToPosition" />: converts an LSP (line, character) position back to an absolute
    ///     offset into <paramref name="text" />.
    /// </summary>
    public static int PositionToOffset(string text, int line, int character)
    {
        var currentLine = 0;
        var i = 0;
        while (currentLine < line && i < text.Length)
        {
            if (text[i] == '\n')
                currentLine++;
            i++;
        }

        return Math.Min(i + character, text.Length);
    }
}