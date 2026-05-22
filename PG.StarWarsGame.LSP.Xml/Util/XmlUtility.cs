// // Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// // Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Xml.Util;

public static class XmlUtility
{
    public static HtmlDocument CreateHtmlDocument(string xmlContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(xmlContent);
        return doc;
    }

    public static int GetLine(HtmlNode? node)
    {
        if (node is null) return -1;
        return node.Line - 1;
    }

    public static int GetOpeningTagStartColumn(HtmlNode? node)
    {
        if (node is null) return -1;
        return node.LinePosition + 1;
    }

    public static int GetOpeningTagEndColumn(HtmlNode? node)
    {
        if (node is null) return -1;
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
        if (node is null) return -1;
        var depth = 0;
        while (node.ParentNode != null)
        {
            depth++;
            node = node.ParentNode;
        }

        return depth;
    }
}