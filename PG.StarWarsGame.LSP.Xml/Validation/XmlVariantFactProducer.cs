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

    /// <summary>
    ///     Sweeps the engine gives variant resolution before it stops
    ///     (<c>GameObjectTypeManagerClass::Overlay_Types</c>). A chain this long or shorter resolves
    ///     whatever order things are declared in; longer depends on the order and fails silently.
    /// </summary>
    private const int MaxResolvablePasses = 10;

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

            // Everything about the base CHAIN is reported on the Variant_Of_Existing_Type element,
            // since that is the one line an author would change to fix any of it.
            var marker = FindVariantChild(node) ?? node;

            var effective = resolver.Resolve(variant.Id);
            if (effective.Cyclic)
            {
                facts.Add(new VariantCycleFact(documentUri, XmlUtility.GetLine(marker),
                    XmlUtility.GetOpeningTagStartColumn(marker), marker.Name.Length,
                    variant.Id, effective.CycleObjectId));
                continue; // do not analyse the tags of a cyclic object
            }

            CollectChainFacts(documentUri, variant, effective, marker, facts);

            var baseValues = BaseValues(resolver, variant.VariantBaseId);
            CollectTagFacts(node, documentUri, text, baseValues, effective, facts);
        }

        return facts;
    }

    /// <summary>
    ///     Reports what the base CHAIN does, as opposed to what the variant's own tags do.
    /// </summary>
    /// <remarks>
    ///     Both findings are anchored on the <c>Variant_Of_Existing_Type</c> element, since that is
    ///     the single line an author would change to fix either of them. Only reached for
    ///     non-cyclic objects - a loop is reported on its own and would otherwise collect a second
    ///     complaint about one mistake.
    /// </remarks>
    private static void CollectChainFacts(string documentUri, GameSymbol variant,
        EffectiveObject effective, HtmlNode marker, List<XmlFact> facts)
    {
        var line = XmlUtility.GetLine(marker);
        var col = XmlUtility.GetOpeningTagStartColumn(marker);
        var len = marker.Name.Length;

        // Chain is [this object, ...bases], so anything past the head is a base. A chain that
        // stopped early because a base was missing has fewer entries than the author wrote, which
        // is exactly why the unresolved case is checked first and returns.
        if (!string.IsNullOrEmpty(variant.VariantBaseId) &&
            effective.Chain.Length < 2)
        {
            facts.Add(new VariantBaseUnresolvedFact(documentUri, line, col, len,
                variant.Id, variant.VariantBaseId));
            return;
        }

        var baseCount = effective.Chain.Length - 1;
        if (baseCount > MaxResolvablePasses)
            facts.Add(new VariantChainTooDeepFact(documentUri, line, col, len,
                variant.Id, baseCount, effective.Chain[^1]));
    }

    private void CollectTagFacts(HtmlNode objectNode, string documentUri, string text,
        IReadOnlyDictionary<string, string> baseValues, EffectiveObject effective, List<XmlFact> facts)
    {
        // An additive tag is usually multipleAllowed, so one object commonly carries several
        // occurrences. The merge outcome is a property of the object, not of each occurrence -
        // reporting it once keeps the Problems panel readable.
        var reportedMerges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            if (mode == VariantMode.Merge)
            {
                // Take the appended result from the resolver rather than recomputing it here, so
                // what is reported cannot drift from what the effective-object view and hints show.
                var mergedTag = effective.Tags.FirstOrDefault(t =>
                    t.Provenance == VariantProvenance.Merged &&
                    t.TagName.Equals(child.Name, StringComparison.OrdinalIgnoreCase));
                if (mergedTag?.BaseValue is not null && reportedMerges.Add(child.Name))
                    facts.Add(new VariantAdditiveMergeFact(documentUri, line, col, len, name,
                        mergedTag.BaseValue, mergedTag.Value));
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
                                 string.Equals(XmlUtility.GetNameAttributeValue(n, NameAttribute),
                                     objectId, StringComparison.OrdinalIgnoreCase));
    }

    private HtmlNode? FindVariantChild(HtmlNode objectNode)
    {
        return objectNode.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            schema.GetTag(n.Name)?.SemanticType == TagSemanticType.VariantParent);
    }
}