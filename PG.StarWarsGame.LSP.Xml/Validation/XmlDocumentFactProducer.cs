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
    IXmlStructuralValidator structuralValidator,
    IEnumerable<IXmlCrossTagRule>? crossTagRules = null)
    : IXmlDocumentFactProducer
{
    private readonly IReadOnlyList<IXmlCrossTagRule> _crossTagRules = crossTagRules?.ToList() ?? [];

    public IReadOnlyList<XmlFact> Produce(ParsedXmlDocument document, string documentUri)
    {
        var facts = new List<XmlFact>();

        foreach (var error in structuralValidator.Validate(document.Text))
            facts.Add(new XmlStructureFact(documentUri, error.Line, error.Column, 1, error.Reason));

        var doc = document.Html;
        var lineIndex = document.LineIndex;

        var isTypeContainerLevel = IsTypeContainerDocument(documentUri);
        TagResolutionContext? initialContext = null;
        if (!isTypeContainerLevel)
        {
            var fileTypes = fileTypeRegistry.GetTypesForFile(fileHelper.NormalizeUri(documentUri));
            if (!fileTypes.IsEmpty)
            {
                var rootNode = doc.DocumentNode.ChildNodes
                    .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
                if (rootNode is not null)
                    initialContext = new TagResolutionContext(fileTypes[0], 0, rootNode);
            }
        }

        foreach (var root in doc.DocumentNode.ChildNodes)
        {
            if (root.NodeType != HtmlNodeType.Element) continue;
            WalkNodes(root, facts, lineIndex, isTypeContainerLevel, documentUri, initialContext, document.Text);
        }

        // Collect notes hints for every element in the document
        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var tag = schema.GetTag(node.Name);
            if (tag is null || tag.Notes.Count == 0) continue;
            facts.Add(new XmlNotesFact(documentUri, XmlUtility.GetLine(node), 0, 0, tag));
        }

        return facts;
    }

    private bool IsTypeContainerDocument(string documentUri)
    {
        var fileTypes = fileTypeRegistry.GetTypesForFile(fileHelper.NormalizeUri(documentUri));
        return !fileTypes.IsEmpty && fileTypes.Any(t => schema.GetObjectType(t)?.NameTag is not null);
    }

    private void WalkNodes(
        HtmlNode node, List<XmlFact> facts, LineOffsetIndex lineIndex, bool isTypeContainerLevel,
        string documentUri, TagResolutionContext? context, string text)
    {
        if (isTypeContainerLevel)
        {
            foreach (var child in node.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var typeName = schema.GetObjectType(child.Name)?.TypeName
                               ?? schema.GetObjectType(XmlUtility.ToPascalCase(child.Name))?.TypeName;
                var childContext = typeName is not null
                    ? new TagResolutionContext(typeName, XmlUtility.GetDepth(child), child, context)
                    : context;
                WalkNodes(child, facts, lineIndex, false, documentUri, childContext, text);
            }

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
            var tagDef = ResolveTag(name, context);
            if (tagDef is not null && !tagDef.MultipleAllowed)
                duplicatedSingletons.Add(name);
        }

        // Pass 2: emit facts for each child
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;
            var name = child.Name;
            var tagDef = ResolveTag(name, context);

            // AbilityDefinitionSubObjectList: each child element name IS the ability schema type (PascalCase).
            // GuiActivatedAbilityDefinitionSubObjectList: all children are the same ability type (Unit_Ability → UnitAbility).
            if (tagDef?.ValueType is XmlValueType.AbilityDefinitionSubObjectList
                or XmlValueType.GuiActivatedAbilityDefinitionSubObjectList)
            {
                foreach (var abilityNode in child.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
                {
                    var abilityTypeName = XmlUtility.ToPascalCase(abilityNode.Name);
                    var abilityContext = new TagResolutionContext(
                        abilityTypeName, XmlUtility.GetDepth(abilityNode), abilityNode, context);
                    WalkNodes(abilityNode, facts, lineIndex, false, documentUri, abilityContext, text);
                }

                continue;
            }

            if (tagDef is null)
            {
                WalkNodes(child, facts, lineIndex, false, documentUri, context, text);
                continue;
            }

            var line0 = XmlUtility.GetLine(child);
            var col0 = XmlUtility.GetTagBracketColumn(child);

            if (duplicatedSingletons.Contains(name))
            {
                var group = childGroups[name];
                var otherLines = group
                    .Where(n => !ReferenceEquals(n, child))
                    .Select(n => n.Line)
                    .ToList();
                var openLen = XmlUtility.GetOpeningTagLength(child);
                // Whole-element span so the Unnecessary grey-out covers the entire dead node.
                var (endLine, endCol) = XmlUtility.GetElementEndPosition(child, text);
                facts.Add(new XmlDuplicateTagFact(documentUri, line0, col0, openLen, tagDef, otherLines,
                    ReferenceEquals(child, group[^1]), endLine, endCol));
                WalkNodes(child, facts, lineIndex, false, documentUri, context, text);
                continue;
            }

            var rawValue = child.InnerText.Trim();
            if (!string.IsNullOrEmpty(rawValue))
            {
                var innerHtml = child.InnerHtml;
                var leadingWs = innerHtml.Length - innerHtml.TrimStart().Length;
                var (valLine, valCol) = lineIndex.GetPosition(child.InnerStartIndex + leadingWs);
                facts.Add(new XmlTagValueFact(documentUri, valLine, valCol, rawValue.Length, tagDef, rawValue));
            }

            WalkNodes(child, facts, lineIndex, false, documentUri, context, text);
        }

        // Pass 3: cross-tag rules evaluated on the current object's full child set
        if (_crossTagRules.Count > 0)
        {
            var readOnly = childGroups.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<HtmlNode>)kv.Value,
                StringComparer.OrdinalIgnoreCase);
            foreach (var rule in _crossTagRules)
                facts.AddRange(rule.Evaluate(node, readOnly, documentUri));
        }
    }

    private XmlTagDefinition? ResolveTag(string name, TagResolutionContext? context)
    {
        return XmlTagResolver.Resolve(schema, name, context);
    }
}