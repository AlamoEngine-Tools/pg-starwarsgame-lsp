// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlDocumentFactProducer(
    IFileHelper fileHelper,
    ISchemaProvider schema,
    IFileTypeRegistry fileTypeRegistry,
    IXmlStructuralValidator structuralValidator)
    : IXmlDocumentFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string xmlText, string documentUri)
    {
        var facts = new List<XmlFact>();

        foreach (var error in structuralValidator.Validate(xmlText))
            facts.Add(new XmlStructureFact(documentUri, error.Line, error.Column, 1, error.Reason));

        var doc = XmlUtility.CreateHtmlDocument(xmlText);
        var lineNum = doc.DocumentNode.EndNode.Line;
        var lines = xmlText.Split('\n');

        var isTypeContainerLevel = IsTypeContainerDocument(documentUri);

        foreach (var root in doc.DocumentNode.ChildNodes)
        {
            if (root.NodeType != HtmlNodeType.Element) continue;
            WalkNodes(root, facts, xmlText, lines, isTypeContainerLevel, documentUri);
        }

        // Collect notes hints for every element in the document
        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var tag = schema.GetTag(node.Name);
            if (tag is null || tag.Notes.Count == 0) continue;
            var line0 = Math.Max(0, node.Line - 1);
            facts.Add(new XmlNotesFact(documentUri, line0, 0, 0, tag));
        }

        return facts;
    }

    private bool IsTypeContainerDocument(string documentUri)
    {
        var fileTypes = fileTypeRegistry.GetTypesForFile(fileHelper.NormalizeUri(documentUri));
        return !fileTypes.IsEmpty && fileTypes.Any(t => schema.GetObjectType(t)?.NameTag is not null);
    }

    private void WalkNodes(
        HtmlNode node, List<XmlFact> facts, string text, string[] lines, bool isTypeContainerLevel, string documentUri)
    {
        if (isTypeContainerLevel)
        {
            foreach (var child in node.ChildNodes)
                if (child.NodeType == HtmlNodeType.Element)
                    WalkNodes(child, facts, text, lines, false, documentUri);
            return;
        }

        // Pass 1: group direct children by name for duplicate detection
        var childGroups = new Dictionary<string, List<HtmlNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;
            if (!childGroups.TryGetValue(child.Name, out var list))
                childGroups[child.Name] = list = [];
            list.Add(child);
        }

        var duplicatedSingletons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, nodes) in childGroups)
        {
            if (nodes.Count <= 1) continue;
            var tagDef = schema.GetTag(name);
            if (tagDef is not null && !tagDef.MultipleAllowed)
                duplicatedSingletons.Add(name);
        }

        // Pass 2: emit facts for each child
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;
            var name = child.Name;
            var tagDef = schema.GetTag(name);

            if (tagDef is null)
            {
                WalkNodes(child, facts, text, lines, false, documentUri);
                continue;
            }

            var (line0, col0) = ComputePosition(child, lines);

            if (duplicatedSingletons.Contains(name))
            {
                var otherLines = childGroups[name]
                    .Where(n => !ReferenceEquals(n, child))
                    .Select(n => n.Line)
                    .ToList();
                var openLen = ComputeOpeningTagLength(child, col0, line0 < lines.Length ? lines[line0] : string.Empty);
                facts.Add(new XmlDuplicateTagFact(documentUri, line0, col0, openLen, tagDef, otherLines));
                WalkNodes(child, facts, text, lines, false, documentUri);
                continue;
            }

            var rawValue = child.InnerText.Trim();
            if (!string.IsNullOrEmpty(rawValue))
            {
                var innerHtml = child.InnerHtml;
                var leadingWs = innerHtml.Length - innerHtml.TrimStart().Length;
                var (valLine, valCol) = ComputeOffsetToLineCol(text, child.InnerStartIndex + leadingWs);
                facts.Add(new XmlTagValueFact(documentUri, valLine, valCol, rawValue.Length, tagDef, rawValue));
            }

            WalkNodes(child, facts, text, lines, false, documentUri);
        }
    }

    private static (int line0, int col0) ComputePosition(HtmlNode node, string[] lines)
    {
        var line0 = Math.Max(0, node.Line - 1);
        var sourceLine = line0 < lines.Length ? lines[line0].TrimEnd('\r') : string.Empty;
        var col0 = sourceLine.IndexOf('<');
        if (col0 < 0) col0 = 0;
        return (line0, col0);
    }

    private static (int line, int col) ComputeOffsetToLineCol(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }

        return (line, offset - lineStart);
    }

    private static int ComputeOpeningTagLength(HtmlNode node, int col0, string sourceLine)
    {
        var closeAngle = sourceLine.IndexOf('>', col0);
        return closeAngle >= 0 ? closeAngle + 1 - col0 : node.Name.Length + 2;
    }
}