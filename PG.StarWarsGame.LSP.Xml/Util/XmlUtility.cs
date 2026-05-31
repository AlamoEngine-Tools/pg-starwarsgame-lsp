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
}