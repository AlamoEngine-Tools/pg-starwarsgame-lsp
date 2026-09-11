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

        var fileTypes = fileTypeRegistry.GetTypesForFile(fileHelper.NormalizeUri(documentUri));
        var isTypeContainerLevel = !fileTypes.IsEmpty &&
                                   fileTypes.Any(t => schema.GetObjectType(t)?.NameTag is not null);

        TagResolutionContext? initialContext = null;
        if (!isTypeContainerLevel && !fileTypes.IsEmpty)
        {
            var rootNode = doc.DocumentNode.ChildNodes
                .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
            if (rootNode is not null)
                initialContext = new TagResolutionContext(fileTypes[0], 0, rootNode);
        }

        var state = new WalkState(
            documentUri, document.Text, lineIndex, facts,
            // Asked of the schema rather than hardcoded: the variant tag is declared on exactly one
            // type, so "does this document's type declare it" IS the engine's rule.
            fileTypes.Any(t => schema.GetTagsForType(t)
                .Any(x => x.SemanticType == TagSemanticType.VariantParent)),
            fileTypes.IsEmpty ? null : fileTypes[0]);

        foreach (var root in doc.DocumentNode.ChildNodes)
        {
            if (root.NodeType != HtmlNodeType.Element) continue;
            // In a singleton document the root element IS the object, so its children are tags. In
            // a type container they are objects, and the recursion below raises the flag instead.
            WalkNodes(root, state, initialContext, isTypeContainerLevel,
                !isTypeContainerLevel && initialContext is not null);
        }

        if (isTypeContainerLevel)
            CollectUnnamedObjects(doc, facts, lineIndex, documentUri);

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

    /// <summary>
    ///     Flags object elements that carry no usable name.
    /// </summary>
    /// <remarks>
    ///     Deliberately mirrors <c>XmlGameDocumentParser.CollectSymbolsFromRegistry</c>: the first
    ///     registered type that the schema resolves, the first element of the document as the
    ///     container, and every ELEMENT child of it as an object. Walking a different shape here
    ///     would report objects the parser never tried to index, or miss the ones it dropped. Only
    ///     element children are considered, which is what keeps the 44 vanilla files whose
    ///     <c>Name=""</c> sits inside a comment block silent.
    /// </remarks>
    private void CollectUnnamedObjects(
        HtmlDocument doc, List<XmlFact> facts, LineOffsetIndex lineIndex, string documentUri)
    {
        var typeDef = fileTypeRegistry.GetTypesForFile(fileHelper.NormalizeUri(documentUri))
            .Select(t => schema.GetObjectType(t))
            .FirstOrDefault(t => t?.NameTag is not null);
        if (typeDef?.NameTag is null) return;

        var rootContainer = doc.DocumentNode.ChildNodes
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
        if (rootContainer is null) return;

        foreach (var node in rootContainer.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            // HAP lowercases attribute names, so match the name tag case-insensitively.
            var attr = node.Attributes.FirstOrDefault(a =>
                a.Name.Equals(typeDef.NameTag, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(attr?.Value)) continue;

            facts.Add(new XmlUnnamedObjectFact(
                documentUri,
                XmlUtility.GetLine(node),
                XmlUtility.GetTagBracketColumn(node),
                node.Name.Length + 1,
                typeDef.TypeName,
                typeDef.NameTag,
                node.Name));
        }
    }

    /// <param name="isObjectLevel">
    ///     Whether <paramref name="node" /> is an OBJECT element, so its direct element children are
    ///     tags and can be judged against the tag vocabulary. False everywhere below that: the
    ///     contents of a tag are described by its value type, not by the tag table.
    /// </param>
    private void WalkNodes(
        HtmlNode node, WalkState state, TagResolutionContext? context, bool isTypeContainerLevel,
        bool isObjectLevel)
    {
        var facts = state.Facts;
        var documentUri = state.DocumentUri;
        var lineIndex = state.LineIndex;
        var text = state.Text;

        if (isTypeContainerLevel)
        {
            foreach (var child in node.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var typeName = schema.GetObjectType(child.Name)?.TypeName
                               ?? schema.GetObjectType(XmlUtility.ToPascalCase(child.Name))?.TypeName;
                var childContext = typeName is not null
                    ? new TagResolutionContext(typeName, XmlUtility.GetDepth(child), child, context)
                    : context;
                WalkNodes(child, state, childContext, false, true);
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
                    WalkNodes(abilityNode, state, abilityContext, false, true);
                }

                continue;
            }

            if (tagDef is null)
            {
                if (isObjectLevel && !IsStructuralContainer(child))
                    facts.Add(UnknownTag(child, node, documentUri, text));
                WalkNodes(child, state, context, false, false);
                continue;
            }

            // Variant derivation exists for GameObjectType and nothing else. The resolver's flat
            // fallback means the tag resolves on any element, so the unknown-tag rule never sees
            // this - it needs asking separately.
            if (isObjectLevel && tagDef.SemanticType == TagSemanticType.VariantParent &&
                !state.VariantSupported && state.OwnerTypeName is { } ownerType)
            {
                var authored = XmlUtility.GetOriginalTagName(child, text);
                facts.Add(new VariantTagNotSupportedFact(documentUri, XmlUtility.GetLine(child),
                    XmlUtility.GetTagBracketColumn(child), authored.Length + 1, authored, ownerType));
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
                var isLast = ReferenceEquals(child, group[^1]);
                // Whole-element span so the Unnecessary grey-out covers the entire dead node.
                var (endLine, endCol) = XmlUtility.GetElementEndPosition(child, text);
                facts.Add(new XmlDuplicateTagFact(documentUri, line0, col0, openLen, tagDef, otherLines,
                    isLast, endLine, endCol));

                // The duplicate is a complaint about WHERE the value sits and says nothing about
                // whether it is valid, so the value still has to be checked - otherwise a
                // duplicated tag silently escapes every value rule and the second, unrelated error
                // only surfaces once the duplicate is fixed.
                //
                // Only the last occurrence, because that is the one the engine reads: reporting a
                // bad enum on a line the game never looks at would be a complaint about dead text,
                // and those lines are already greyed out as Unnecessary.
                if (isLast)
                {
                    var duplicateValue = child.InnerText.Trim();
                    if (!string.IsNullOrEmpty(duplicateValue))
                    {
                        var (dupLine, dupCol, dupLen) = XmlUtility.GetValuePosition(child, lineIndex);
                        facts.Add(new XmlTagValueFact(
                            documentUri, dupLine, dupCol, dupLen, tagDef, duplicateValue));
                    }
                }

                WalkNodes(child, state, context, false, false);
                continue;
            }

            var rawValue = child.InnerText.Trim();
            if (!string.IsNullOrEmpty(rawValue))
            {
                var (valLine, valCol, valLen) = XmlUtility.GetValuePosition(child, lineIndex);
                facts.Add(new XmlTagValueFact(documentUri, valLine, valCol, valLen, tagDef, rawValue));
            }

            WalkNodes(child, state, context, false, false);
        }

        // Pass 3: cross-tag rules evaluated on the current object's full child set
        if (_crossTagRules.Count > 0)
        {
            var readOnly = childGroups.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<HtmlNode>)kv.Value,
                StringComparer.OrdinalIgnoreCase);
            foreach (var rule in _crossTagRules)
                facts.AddRange(rule.Evaluate(node, readOnly, documentUri, lineIndex));
        }
    }

    /// <summary>
    ///     The values that are fixed for one document, carried through the recursion together.
    /// </summary>
    /// <param name="VariantSupported">
    ///     Whether any type this document is registered for declares the variant-parent tag. Asked
    ///     of the schema rather than hardcoded to <c>GameObjectType</c>, so the rule follows the
    ///     schema if the engine mapping is ever extended.
    /// </param>
    /// <param name="OwnerTypeName">
    ///     The document's first registered type, used to name the owner in messages. Null when the
    ///     registry types the document not at all, which is what keeps those documents silent.
    /// </param>
    private sealed record WalkState(
        string DocumentUri,
        string Text,
        LineOffsetIndex LineIndex,
        List<XmlFact> Facts,
        bool VariantSupported,
        string? OwnerTypeName);

    private XmlTagDefinition? ResolveTag(string name, TagResolutionContext? context)
    {
        return XmlTagResolver.Resolve(schema, name, context);
    }

    /// <summary>
    ///     Whether an unresolved element is a grouping element rather than a mistyped tag.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Some documents wrap their tags in a level the schema does not model, because the
    ///         engine reads straight through it: <c>&lt;RadarMap&gt;</c> holds
    ///         <c>&lt;RadarMapEvents&gt;</c> and <c>&lt;RadarMapSettings&gt;</c>, and the tags our
    ///         <c>RadarMap</c> type declares sit inside those. Reporting the wrapper would be a
    ///         complaint about the shape of a shipped file that loads correctly.
    ///     </para>
    ///     <para>
    ///         The discriminator is the value: a tag exists to carry one, so a mistyped tag still
    ///         has its text. An element that holds only other elements is carrying structure.
    ///     </para>
    /// </remarks>
    private static bool IsStructuralContainer(HtmlNode node)
    {
        var hasElementChildren = false;
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Element) hasElementChildren = true;
            else if (child.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(child.InnerText))
                return false;
        }

        return hasElementChildren;
    }

    /// <summary>
    ///     Builds the observation for an element the schema has no tag by that name for.
    /// </summary>
    /// <remarks>
    ///     The name is taken from the document rather than from the node, because HAP lower-cases
    ///     element names and the diagnostic quotes the tag back at the reader.
    /// </remarks>
    private XmlUnknownTagFact UnknownTag(
        HtmlNode child, HtmlNode owner, string documentUri, string text)
    {
        var authored = XmlUtility.GetOriginalTagName(child, text);
        return new XmlUnknownTagFact(
            documentUri,
            XmlUtility.GetLine(child),
            XmlUtility.GetTagBracketColumn(child),
            authored.Length + 1,
            authored,
            XmlUtility.GetOriginalTagName(owner, text),
            TagNameSuggester.Suggest(schema, authored));
    }
}