// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Parsing;

public sealed class XmlGameDocumentParser : IGameDocumentParser
{
    private readonly ILspConfigurationProvider? _configProvider;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly ILogger<XmlGameDocumentParser> _logger;
    private readonly IXmlParseCache? _parseCache;
    private readonly ISchemaProvider _schema;

    // parseCache is optional so minimal test setups can omit it; production wires the shared
    // cache so the indexing parse seeds it — the diagnostics publish and the first hover/inlay
    // request after an edit then reuse this parse instead of re-parsing. A null configProvider
    // (test convenience) means every feature flag reads as enabled.
    public XmlGameDocumentParser(IFileHelper fileHelper, ISchemaProvider schema,
        IFileTypeRegistry fileTypeRegistry, ILogger<XmlGameDocumentParser> logger,
        IXmlParseCache? parseCache = null, ILspConfigurationProvider? configProvider = null)
    {
        _fileHelper = fileHelper;
        _schema = schema;
        _fileTypeRegistry = fileTypeRegistry;
        _logger = logger;
        _parseCache = parseCache;
        _configProvider = configProvider;
    }

    public bool CanParse(string fileExtension)
    {
        return fileExtension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<DocumentIndex> ParseAsync(
        string documentUri, string text, int version, CancellationToken ct)
    {
        var canonicalUri = _fileHelper.NormalizeUri(documentUri);
        var parsed = _parseCache?.GetOrParse(canonicalUri, text) ?? ParsedXmlDocument.Parse(text);
        var doc = parsed.Html;

        var registeredTypes = _fileTypeRegistry.GetTypesForFile(canonicalUri);
        var lineIndex = parsed.LineIndex;

        // References are collected first so the symbol passes can append the typed variant-base
        // reference for each object they index (the enclosing object's type is only known here).
        var references = CollectReferences(doc, canonicalUri, lineIndex, ct);
        var symbols = CollectSymbolsFromRegistry(doc, canonicalUri, lineIndex, registeredTypes, references, ct);
        symbols.AddRange(CollectSubObjectListSymbols(doc, canonicalUri, lineIndex, references, ct));

        if ((_configProvider?.Current.Features.Story.Symbols ?? true) &&
            registeredTypes.Contains("StoryParser", StringComparer.OrdinalIgnoreCase))
            StoryDocumentSymbolCollector.Collect(parsed, canonicalUri, _schema, symbols, references);

        var groupMemberships = CollectGroupMemberships(doc, canonicalUri, lineIndex, ct);

        return ValueTask.FromResult(new DocumentIndex(
            canonicalUri, version,
            symbols.ToImmutableArray(),
            references.ToImmutableArray(),
            GroupMemberships: groupMemberships.ToImmutableArray()));
    }

    private List<GameSymbol> CollectSymbolsFromRegistry(HtmlDocument doc, string documentUri,
        LineOffsetIndex lineIndex, ImmutableArray<string> registeredTypes, List<GameReference> references,
        CancellationToken ct)
    {
        var typeDef = registeredTypes
            .Select(t => _schema.GetObjectType(t))
            .FirstOrDefault(t => t is not null);

        if (typeDef?.NameTag is null) return [];

        var symbols = new List<GameSymbol>();
        var rootContainer = doc.DocumentNode.ChildNodes
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
        if (rootContainer is null) return symbols;

        foreach (var node in rootContainer.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            ct.ThrowIfCancellationRequested();
            var id = GetNameAttribute(node, typeDef.NameTag);
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogDebug("Type '{Type}' element at line {Line} has no Name attribute — skipped",
                    typeDef.TypeName, node.Line);
            }
            else
            {
                var col = FindNameAttributeValueColumn(node, typeDef.NameTag, lineIndex);
                var (variantBaseId, variantRef) = ResolveVariant(node, typeDef.TypeName, documentUri, lineIndex);
                if (variantRef is not null) references.Add(variantRef);
                symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject, typeDef.TypeName,
                    new FileOrigin(documentUri, node.Line - 1, col), null, variantBaseId));
            }
        }

        return symbols;
    }

    /// <summary>
    ///     Detects a <c>Variant_Of_Existing_Type</c> child (a tag with
    ///     <see cref="TagSemanticType.VariantParent" />) on an object node and returns its base id plus
    ///     a typed <see cref="GameReference" /> to that base. <paramref name="enclosingTypeName" /> is the
    ///     variant object's own type, so the base must be of the same type — this lets the existing
    ///     unresolved-reference and type-mismatch handlers validate the inheritance link for free.
    /// </summary>
    private (string? BaseId, GameReference? Reference) ResolveVariant(
        HtmlNode objectNode, string enclosingTypeName, string documentUri, LineOffsetIndex lineIndex,
        string? ownerPrefix = null)
    {
        foreach (var child in objectNode.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var tagDef = _schema.GetTag(child.Name);
            if (tagDef?.SemanticType != TagSemanticType.VariantParent) continue;

            var innerText = child.InnerText;
            var trimmed = innerText.Trim();
            if (trimmed.Length == 0) return (null, null);

            var tokenOffset = innerText.IndexOf(trimmed, StringComparison.Ordinal);
            var absPos = child.InnerStartIndex + tokenOffset;
            var (line, column) = lineIndex.GetPosition(absPos);

            // The base id/reference must live in the same id-space as objectNode's own id — for
            // top-level objects that's the bare name (ownerPrefix is null); for abilities it's
            // owner-scoped ("{ownerId}$Name", see CollectSubObjectListSymbols) to avoid coincidentally
            // resolving to an unrelated object's same-named ability.
            var baseId = ownerPrefix is not null ? $"{ownerPrefix}${trimmed}" : trimmed;
            var reference = new GameReference(baseId, GameSymbolKind.XmlObject,
                enclosingTypeName, documentUri, line, column, trimmed.Length);
            return (baseId, reference);
        }

        return (null, null);
    }

    private static int? FindNameAttributeValueColumn(HtmlNode node, string nameTag, LineOffsetIndex lineIndex)
    {
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals(nameTag, StringComparison.OrdinalIgnoreCase));
        if (attr is null) return null;
        return lineIndex.GetPosition(attr.ValueStartIndex).Col;
    }

    private List<GameReference> CollectReferences(HtmlDocument doc, string documentUri, LineOffsetIndex lineIndex,
        CancellationToken ct)
    {
        var references = new List<GameReference>();
        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var child in node.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var tagDef = _schema.GetTag(child.Name);
                if (tagDef is null) continue;

                if (tagDef.ReferenceKind == ReferenceKind.Enum &&
                    tagDef.Enum?.Kind == EnumKind.DynamicXml)
                {
                    if (HasChildElement(child)) continue;
                    CollectEnumReferences(child, tagDef.Enum.Name, lineIndex, documentUri, references);
                    continue;
                }

                // Presence_Induced_Animations: "AnimationStateId, ObjectName, ..." — the first
                // token is an engine animation state (not indexable), the rest are game objects
                // whose presence triggers it. Record those as object references so
                // go-to-definition and unresolved-reference validation cover them.
                if (tagDef.ValidationOverride?.ValidationId == "presence-induced-animations")
                {
                    if (HasChildElement(child)) continue;
                    CollectPresenceInducedObjectReferences(child, lineIndex, documentUri, references);
                    continue;
                }

                // (GameObjectCategoryType, float) tuples: record slot 0 as an enum: reference
                // so go-to-definition/rename work on the category token. Membership is validated
                // by InaccuracyMapHandler; XmlIndexFactProducer skips enum: ids.
                if (tagDef.ValueType == XmlValueType.InaccuracyMap)
                {
                    if (HasChildElement(child)) continue;
                    CollectInaccuracyCategoryReference(child, lineIndex, documentUri, references);
                    continue;
                }

                if (tagDef.ReferenceKind != ReferenceKind.XmlObject) continue;
                if (tagDef.SemanticType == TagSemanticType.ReferenceGroup) continue;
                // Variant base references are emitted by the symbol passes with the enclosing
                // object's type as ExpectedTypeName; skip here to avoid a duplicate wildcard-typed one.
                if (tagDef.SemanticType == TagSemanticType.VariantParent) continue;
                // A reference value is leaf text. An element that itself contains child elements is an
                // object definition whose tag name collides with a reference tag (e.g. the
                // <Faction Name="X">…</Faction> container vs. a <Faction>X</Faction> reference) — using
                // its InnerText would capture the whole object as one bogus reference.
                if (HasChildElement(child)) continue;

                var innerText = child.InnerText;
                var ownerPrefix = tagDef.SemanticType == TagSemanticType.OwnerScopedReference
                    ? FindEnclosingObjectId(node)
                    : null;

                foreach (var (name, tokenOffset) in SplitReferenceNames(tagDef, innerText))
                {
                    var absPos = child.InnerStartIndex + tokenOffset;
                    var (line, column) = lineIndex.GetPosition(absPos);
                    var targetId = ownerPrefix is not null ? $"{ownerPrefix}${name}" : name;

                    references.Add(new GameReference(
                        targetId,
                        GameSymbolKind.XmlObject,
                        tagDef.ObjectType?.TypeName,
                        documentUri,
                        line,
                        column,
                        name.Length));
                }
            }
        }

        return references;
    }

    // Every token AFTER the leading animation-state id of a Presence_Induced_Animations value,
    // as wildcard-typed object references.
    private static void CollectPresenceInducedObjectReferences(HtmlNode child,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        var first = true;
        foreach (var (token, tokenOffset) in XmlUtility.SplitListWithOffsets(innerText))
        {
            if (first)
            {
                first = false; // the animation state id — not a game object
                continue;
            }

            var absPos = child.InnerStartIndex + tokenOffset;
            var (line, column) = lineIndex.GetPosition(absPos);
            references.Add(new GameReference(
                token,
                GameSymbolKind.XmlObject,
                null,
                documentUri,
                line,
                column,
                token.Length));
        }
    }

    // Slot 0 of an InaccuracyMap tuple ("Bomber, 15.0") as an enum: reference, mirroring
    // CollectEnumReferences' id format for plain dynamic-enum tags.
    private static void CollectInaccuracyCategoryReference(HtmlNode child,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        var comma = innerText.IndexOf(',');
        var slot = comma < 0 ? innerText : innerText[..comma];
        var token = slot.Trim();
        if (token.Length == 0) return;

        var tokenOffset = slot.IndexOf(token, StringComparison.Ordinal);
        var absPos = child.InnerStartIndex + tokenOffset;
        var (line, column) = lineIndex.GetPosition(absPos);
        references.Add(new GameReference(
            $"enum:GameObjectCategoryType/{token}",
            null,
            null,
            documentUri,
            line,
            column,
            token.Length));
    }

    private static void CollectEnumReferences(HtmlNode child, string enumName,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        foreach (var (token, tokenOffset) in XmlUtility.SplitListWithOffsets(innerText))
        {
            var absPos = child.InnerStartIndex + tokenOffset;
            var (line, column) = lineIndex.GetPosition(absPos);
            references.Add(new GameReference(
                $"enum:{enumName}/{token}",
                null,
                null,
                documentUri,
                line,
                column,
                token.Length));
        }
    }

    private List<DocumentGroupMembership> CollectGroupMemberships(HtmlDocument doc, string documentUri,
        LineOffsetIndex lineIndex, CancellationToken ct)
    {
        var memberships = new List<DocumentGroupMembership>();

        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var child in node.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var tagDef = _schema.GetTag(child.Name);
                if (tagDef?.SemanticType != TagSemanticType.ReferenceGroup) continue;
                if (tagDef.ReferenceKind != ReferenceKind.XmlObject) continue;
                // Skip object-definition containers whose name collides with a reference-group tag.
                if (HasChildElement(child)) continue;

                var innerText = child.InnerText;
                var trimmed = innerText.Trim();
                if (trimmed.Length == 0) continue;

                // Tag-value cursor position
                var tokenOffset = innerText.IndexOf(trimmed, StringComparison.Ordinal);
                var absPos = child.InnerStartIndex + tokenOffset;
                var (tagLine, tagColumn) = lineIndex.GetPosition(absPos);

                // Parent-name navigation target
                var memberTypeName = tagDef.ObjectType?.TypeName;
                var nameTag = memberTypeName is not null
                    ? _schema.GetObjectType(memberTypeName)?.NameTag
                    : null;

                var memberLine = node.Line - 1;
                int? memberColumn = null;
                if (nameTag is not null)
                {
                    var parentId = GetNameAttribute(node, nameTag);
                    if (parentId.Length > 0)
                        memberColumn = FindNameAttributeValueColumn(node, nameTag, lineIndex);
                }

                memberships.Add(new DocumentGroupMembership(
                    new GroupMembership(trimmed, memberTypeName,
                        new FileOrigin(documentUri, memberLine, memberColumn)),
                    tagLine, tagColumn, trimmed.Length));
            }
        }

        return memberships;
    }

    // Walks up from node to find the nearest ancestor that identifies a game object, then returns
    // that ancestor's name-attribute value. Used for owner-scoped ability symbols and
    // OwnerScopedReference tags. An ancestor qualifies when its element name is a registered
    // object type (whose NameTag then applies) OR when it simply carries a Name attribute — real
    // game files use concrete element names (<SpaceUnit>, <SpecialStructure>, …) that are NOT
    // schema object types (the schema models one umbrella GameObjectType), so without the
    // Name-attribute fallback every owner lookup fails and ability ids collide across objects.
    private string? FindEnclosingObjectId(HtmlNode node)
    {
        var current = node.ParentNode;
        while (current is { NodeType: HtmlNodeType.Element })
        {
            var typeDef = _schema.GetObjectType(XmlUtility.ToPascalCase(current.Name));
            if (typeDef?.NameTag is not null)
            {
                var id = GetNameAttribute(current, typeDef.NameTag);
                return string.IsNullOrEmpty(id) ? null : id;
            }

            var nameAttr = GetNameAttribute(current, "Name");
            if (!string.IsNullOrEmpty(nameAttr))
                return nameAttr;

            current = current.ParentNode;
        }

        return null;
    }

    private static bool HasChildElement(HtmlNode node)
    {
        return node.ChildNodes.Any(n => n.NodeType == HtmlNodeType.Element);
    }

    private static string GetNameAttribute(HtmlNode node, string nameTag)
    {
        // HAP lowercases attribute names; match case-insensitively.
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals(nameTag, StringComparison.OrdinalIgnoreCase));
        return attr?.Value?.Trim() ?? string.Empty;
    }

    private static IEnumerable<(string Name, int Offset)> SplitReferenceNames(
        XmlTagDefinition tagDef, string innerText)
    {
        var multiValue = tagDef.SemanticType == TagSemanticType.PrerequisiteExpression
                         || tagDef.ValueType is XmlValueType.GameObjectTypeReferenceList
                             or XmlValueType.TypeReferenceList
                             or XmlValueType.NameReferenceList
                             or XmlValueType.PerFactionObjectList;

        if (!multiValue)
        {
            var trimmed = innerText.Trim();
            if (trimmed.Length > 0)
                yield return (trimmed, innerText.IndexOf(trimmed, StringComparison.Ordinal));
            yield break;
        }

        foreach (var (token, offset) in XmlUtility.SplitListWithOffsets(innerText))
            yield return (token, offset);
    }

    private List<GameSymbol> CollectSubObjectListSymbols(
        HtmlDocument doc, string documentUri, LineOffsetIndex lineIndex, List<GameReference> references,
        CancellationToken ct)
    {
        var symbols = new List<GameSymbol>();

        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var child in node.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var tagDef = _schema.GetTag(child.Name);
                if (tagDef?.ValueType != XmlValueType.AbilityDefinitionSubObjectList) continue;

                // Find the enclosing game object's ID so abilities can be scoped to their owner,
                // preventing false duplicate-symbol errors when two units share an ability name.
                // Same resolution as OwnerScopedReference tags (incl. the Name-attribute fallback
                // for concrete element names that are not schema object types).
                var ownerId = FindEnclosingObjectId(child);

                foreach (var abilityNode in child.ChildNodes
                             .Where(n => n.NodeType == HtmlNodeType.Element))
                {
                    var typeName = XmlUtility.ToPascalCase(abilityNode.Name);
                    var objectType = _schema.GetObjectType(typeName);
                    if (objectType?.NameTag is null) continue;

                    var abilityName = GetNameAttribute(abilityNode, objectType.NameTag);
                    if (string.IsNullOrEmpty(abilityName)) continue;

                    var ownerPrefix = string.IsNullOrEmpty(ownerId) ? null : ownerId;
                    var id = ownerPrefix is null ? abilityName : $"{ownerPrefix}${abilityName}";
                    var col = FindNameAttributeValueColumn(abilityNode, objectType.NameTag, lineIndex);
                    var (variantBaseId, variantRef) =
                        ResolveVariant(abilityNode, typeName, documentUri, lineIndex, ownerPrefix);
                    if (variantRef is not null) references.Add(variantRef);
                    symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
                        new FileOrigin(documentUri, abilityNode.Line - 1, col), null, variantBaseId));
                }
            }
        }

        return symbols;
    }
}