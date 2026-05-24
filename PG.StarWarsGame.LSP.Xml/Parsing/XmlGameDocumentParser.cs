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
        var symbols = CollectSymbolsFromRegistry(doc, canonicalUri, registeredTypes, ct);

        var references = CollectReferences(doc, canonicalUri, text, ct);

        return ValueTask.FromResult(new DocumentIndex(
            canonicalUri, version,
            symbols.ToImmutableArray(),
            references.ToImmutableArray()));
    }

    private List<GameSymbol> CollectSymbolsFromRegistry(HtmlDocument doc, string documentUri,
        ImmutableArray<string> registeredTypes, CancellationToken ct)
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
                _logger.LogDebug("Type '{Type}' element at line {Line} has no Name attribute — skipped",
                    typeDef.TypeName, node.Line);
            else
                symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject, typeDef.TypeName,
                    new FileOrigin(documentUri, node.Line - 1, null), null));
        }

        return symbols;
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

                var innerText = child.InnerText;
                foreach (var (name, tokenOffset) in SplitReferenceNames(tagDef, innerText))
                {
                    var absPos = child.InnerStartIndex + tokenOffset;
                    var lineStart = text.LastIndexOf('\n', Math.Max(0, absPos - 1)) + 1;
                    var line = child.Line - 1 + CountNewlines(innerText, tokenOffset);
                    var column = absPos - lineStart;

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

    private static string GetNameAttribute(HtmlNode node, string nameTag)
    {
        // HAP lowercases attribute names; match case-insensitively.
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals(nameTag, StringComparison.OrdinalIgnoreCase));
        return attr?.Value?.Trim() ?? string.Empty;
    }

    // Kept for tests that call it directly. Production code now routes through IFileHelper.NormalizeUri.
    [Obsolete("Use IFileHelper.NormalizeUri instead.")]
    internal static string NormalizeDocumentUri(string uri)
    {
        if (uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            uri = uri[8..];
        else if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            uri = uri[7..];
        return uri.Replace('\\', '/').ToLowerInvariant();
    }

    private static IEnumerable<(string Name, int Offset)> SplitReferenceNames(
        XmlTagDefinition tagDef, string innerText)
    {
        bool multiValue = tagDef.SemanticType == TagSemanticType.PrerequisiteExpression
                       || tagDef.ValueType is XmlValueType.GameObjectTypeReferenceList
                                           or XmlValueType.TypeReferenceList
                                           or XmlValueType.NameReferenceList;
        bool skipFirst = tagDef.ValueType == XmlValueType.PerFactionObjectList;

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
            if (first) { first = false; continue; }
            yield return (token, offset);
        }
    }

    private static int CountNewlines(string text, int upToOffset)
    {
        var count = 0;
        for (var i = 0; i < upToOffset && i < text.Length; i++)
            if (text[i] == '\n')
                count++;
        return count;
    }
}