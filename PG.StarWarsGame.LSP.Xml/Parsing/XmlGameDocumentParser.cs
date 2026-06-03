// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Parsing;

public sealed class XmlGameDocumentParser : IGameDocumentParser
{
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly ILogger<XmlGameDocumentParser> _logger;
    private readonly ISchemaProvider _schema;

    public XmlGameDocumentParser(IFileHelper fileHelper, ISchemaProvider schema,
        IFileTypeRegistry fileTypeRegistry, ILogger<XmlGameDocumentParser> logger)
    {
        _fileHelper = fileHelper;
        _schema = schema;
        _fileTypeRegistry = fileTypeRegistry;
        _logger = logger;
    }

    public bool CanParse(string fileExtension)
    {
        return fileExtension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<DocumentIndex> ParseAsync(
        string documentUri, string text, int version, CancellationToken ct)
    {
        var canonicalUri = _fileHelper.NormalizeUri(documentUri);
        var doc = new HtmlDocument();
        doc.LoadHtml(text);

        var registeredTypes = _fileTypeRegistry.GetTypesForFile(canonicalUri);
        var symbols = CollectSymbolsFromRegistry(doc, canonicalUri, text, registeredTypes, ct);
        symbols.AddRange(CollectSubObjectListSymbols(doc, canonicalUri, text, ct));

        var references = CollectReferences(doc, canonicalUri, text, ct);
        var groupMemberships = CollectGroupMemberships(doc, canonicalUri, text, ct);

        return ValueTask.FromResult(new DocumentIndex(
            canonicalUri, version,
            symbols.ToImmutableArray(),
            references.ToImmutableArray(),
            GroupMemberships: groupMemberships.ToImmutableArray()));
    }

    private List<GameSymbol> CollectSymbolsFromRegistry(HtmlDocument doc, string documentUri,
        string text, ImmutableArray<string> registeredTypes, CancellationToken ct)
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
                var col = FindNameAttributeValueColumn(node, typeDef.NameTag, text);
                symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject, typeDef.TypeName,
                    new FileOrigin(documentUri, node.Line - 1, col), null));
            }
        }

        return symbols;
    }

    private static int? FindNameAttributeValueColumn(HtmlNode node, string nameTag, string text)
    {
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals(nameTag, StringComparison.OrdinalIgnoreCase));
        if (attr is null) return null;
        return XmlUtility.OffsetToPosition(text, attr.ValueStartIndex).Col;
    }

    private List<GameReference> CollectReferences(HtmlDocument doc, string documentUri, string text,
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
                if (tagDef?.ReferenceKind != ReferenceKind.XmlObject) continue;
                if (tagDef.SemanticType == TagSemanticType.ReferenceGroup) continue;

                var innerText = child.InnerText;
                foreach (var (name, tokenOffset) in SplitReferenceNames(tagDef, innerText))
                {
                    var absPos = child.InnerStartIndex + tokenOffset;
                    var (line, column) = XmlUtility.OffsetToPosition(text, absPos);

                    references.Add(new GameReference(
                        name,
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

    private List<DocumentGroupMembership> CollectGroupMemberships(HtmlDocument doc, string documentUri,
        string text, CancellationToken ct)
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

                var innerText = child.InnerText;
                var trimmed = innerText.Trim();
                if (trimmed.Length == 0) continue;

                // Tag-value cursor position
                var tokenOffset = innerText.IndexOf(trimmed, StringComparison.Ordinal);
                var absPos = child.InnerStartIndex + tokenOffset;
                var (tagLine, tagColumn) = XmlUtility.OffsetToPosition(text, absPos);

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
                        memberColumn = FindNameAttributeValueColumn(node, nameTag, text);
                }

                memberships.Add(new DocumentGroupMembership(
                    new GroupMembership(trimmed, memberTypeName,
                        new FileOrigin(documentUri, memberLine, memberColumn)),
                    tagLine, tagColumn, trimmed.Length));
            }
        }

        return memberships;
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
                             or XmlValueType.NameReferenceList;
        var skipFirst = tagDef.ValueType == XmlValueType.PerFactionObjectList;

        if (!multiValue && !skipFirst)
        {
            var trimmed = innerText.Trim();
            if (trimmed.Length > 0)
                yield return (trimmed, innerText.IndexOf(trimmed, StringComparison.Ordinal));
            yield break;
        }

        var first = skipFirst;
        foreach (var (token, offset) in XmlUtility.SplitListWithOffsets(innerText))
        {
            if (first)
            {
                first = false;
                continue;
            }

            yield return (token, offset);
        }
    }

    private List<GameSymbol> CollectSubObjectListSymbols(
        HtmlDocument doc, string documentUri, string text, CancellationToken ct)
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

                foreach (var abilityNode in child.ChildNodes
                             .Where(n => n.NodeType == HtmlNodeType.Element))
                {
                    var typeName = XmlUtility.ToPascalCase(abilityNode.Name);
                    var objectType = _schema.GetObjectType(typeName);
                    if (objectType?.NameTag is null) continue;

                    var id = GetNameAttribute(abilityNode, objectType.NameTag);
                    if (string.IsNullOrEmpty(id)) continue;

                    var col = FindNameAttributeValueColumn(abilityNode, objectType.NameTag, text);
                    symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
                        new FileOrigin(documentUri, abilityNode.Line - 1, col), null));
                }
            }
        }

        return symbols;
    }
}