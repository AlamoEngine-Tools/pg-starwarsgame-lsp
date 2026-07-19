// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlVariantFactProducer(ISchemaProvider schema, IVariantTagSource tagSource)
    : IXmlVariantFactProducer
{
    private const string NameAttribute = "Name";

    public IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        if (!index.Documents.TryGetValue(documentUri, out var docIndex))
            return [];

        var variants = docIndex.Symbols.Where(s => !string.IsNullOrEmpty(s.VariantBaseId)).ToList();
        if (variants.Count == 0)
            return [];

        var text = document.Text;
        var hapDoc = document.Html;
        var resolver = new EffectiveObjectResolver(index, schema, tagSource);
        var facts = new List<XmlFact>();

        foreach (var variant in variants)
        {
            var node = FindObjectNode(hapDoc, variant.Id);
            if (node is null) continue;

            var effective = resolver.Resolve(variant.Id);
            if (effective.Cyclic)
            {
                var marker = FindVariantChild(node) ?? node;
                facts.Add(new VariantCycleFact(documentUri, XmlUtility.GetLine(marker),
                    XmlUtility.GetOpeningTagStartColumn(marker), marker.Name.Length,
                    variant.Id, effective.CycleObjectId));
                continue; // do not analyse the tags of a cyclic object
            }

            var baseValues = BaseValues(resolver, variant.VariantBaseId);
            CollectTagFacts(node, documentUri, text, baseValues, facts);
        }

        return facts;
    }

    private void CollectTagFacts(HtmlNode objectNode, string documentUri, string text,
        IReadOnlyDictionary<string, string> baseValues, List<XmlFact> facts)
    {
        foreach (var child in objectNode.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var tagDef = schema.GetTag(child.Name);
            if (tagDef?.SemanticType == TagSemanticType.VariantParent)
                continue; // the variant-declaration tag itself is not a real tag

            var name = XmlUtility.GetOriginalTagName(child, text); // HAP lowercases child.Name; recover for messages
            var line = XmlUtility.GetLine(child);
            var col = XmlUtility.GetOpeningTagStartColumn(child);
            var len = name.Length;

            var mode = tagDef?.VariantMode ?? VariantMode.Replace;
            if (mode == VariantMode.Ignored)
            {
                facts.Add(new VariantIgnoredOverrideFact(documentUri, line, col, len, name));
                continue;
            }

            if (baseValues.TryGetValue(child.Name, out var baseVal) &&
                string.Equals(baseVal, child.InnerText.Trim(), StringComparison.Ordinal))
            {
                // Grey out the whole node (opening tag through closing tag), not just the opening
                // tag name - the value may span multiple lines.
                var (endLine, endCol) = XmlUtility.GetElementEndPosition(child, text);
                facts.Add(new VariantRedundantOverrideFact(documentUri, line, col, len, name, endLine, endCol));
            }
        }
    }

    private static IReadOnlyDictionary<string, string> BaseValues(EffectiveObjectResolver resolver, string? baseId)
    {
        if (string.IsNullOrEmpty(baseId))
            return new Dictionary<string, string>();

        var baseEffective = resolver.Resolve(baseId);
        if (!baseEffective.Found)
            return new Dictionary<string, string>();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in baseEffective.Tags)
            map[tag.TagName] = tag.Value;
        return map;
    }

    private static HtmlNode? FindObjectNode(HtmlDocument doc, string objectId)
    {
        return doc.DocumentNode.Descendants()
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element &&
                                 string.Equals(GetNameAttribute(n), objectId, StringComparison.OrdinalIgnoreCase));
    }

    private HtmlNode? FindVariantChild(HtmlNode objectNode)
    {
        return objectNode.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            schema.GetTag(n.Name)?.SemanticType == TagSemanticType.VariantParent);
    }

    private static string? GetNameAttribute(HtmlNode node)
    {
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals(NameAttribute, StringComparison.OrdinalIgnoreCase));
        var value = attr?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}