// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Parsing;

public sealed class XmlGameDocumentParser : IGameDocumentParser
{
    private readonly ILogger<XmlGameDocumentParser> _logger;
    private readonly ISchemaProvider _schema;

    public XmlGameDocumentParser(ISchemaProvider schema, ILogger<XmlGameDocumentParser> logger)
    {
        _schema = schema;
        _logger = logger;
    }

    public bool CanParse(string fileExtension)
    {
        return fileExtension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<DocumentIndex> ParseAsync(
        string documentUri, string text, int version, CancellationToken ct)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(text);

        var symbols = new List<GameSymbol>();
        var references = new List<GameReference>();

        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            ct.ThrowIfCancellationRequested();

            var typeDef = _schema.GetObjectType(node.Name);
            if (typeDef is null)
            {
                _logger.LogDebug("Unknown element type '{Name}' — skipped", node.Name);
                continue;
            }

            // Emit symbol only for named types (singletons have no NameTag).
            if (typeDef.NameTag is not null)
            {
                // HAP lowercases attribute names; match case-insensitively.
                var attr = node.Attributes.FirstOrDefault(a =>
                    a.Name.Equals(typeDef.NameTag, StringComparison.OrdinalIgnoreCase));
                var id = attr?.Value?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(id))
                    _logger.LogDebug("Type '{Type}' element at line {Line} has no Name attribute — skipped",
                        typeDef.TypeName, node.Line);
                else
                    // HAP line numbers are 1-based; LSP coordinates are 0-based.
                    symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject, typeDef.TypeName,
                        new FileOrigin(documentUri, node.Line - 1, null), null));
            }

            // Emit references for direct child tags with ReferenceKind.XmlObject.
            foreach (var child in node.ChildNodes
                         .Where(n => n.NodeType == HtmlNodeType.Element))
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
                        tagDef.ReferenceType,
                        documentUri,
                        line,
                        column,
                        name.Length));
                }
            }
        }

        return ValueTask.FromResult(new DocumentIndex(
            documentUri, version,
            symbols.ToImmutableArray(),
            references.ToImmutableArray()));
    }

    private static IEnumerable<(string Name, int Offset)> SplitReferenceNames(
        XmlTagDefinition tagDef, string innerText)
    {
        char[]? separators;
        bool skipFirst;

        if (tagDef.SemanticType == TagSemanticType.PrerequisiteExpression)
        {
            separators = ['|', ',', ' ', '\t', '\r', '\n'];
            skipFirst = false;
        }
        else if (tagDef.ValueType is XmlValueType.GameObjectTypeReferenceList
                 or XmlValueType.TypeReferenceList)
        {
            separators = [',', ' ', '\t', '\r', '\n'];
            skipFirst = false;
        }
        else if (tagDef.ValueType == XmlValueType.PerFactionObjectList)
        {
            separators = [',', ' ', '\t', '\r', '\n'];
            skipFirst = true;
        }
        else
        {
            // Single-value tag — no splitting
            var trimmed = innerText.Trim();
            if (trimmed.Length > 0)
                yield return (trimmed, innerText.IndexOf(trimmed, StringComparison.Ordinal));
            yield break;
        }

        var isFirst = skipFirst;
        var i = 0;
        while (i < innerText.Length)
        {
            while (i < innerText.Length && Array.IndexOf(separators, innerText[i]) >= 0)
                i++;
            if (i >= innerText.Length) break;

            var tokenStart = i;
            while (i < innerText.Length && Array.IndexOf(separators, innerText[i]) < 0)
                i++;

            var token = innerText[tokenStart..i];
            if (token.Length > 0)
            {
                if (isFirst)
                    isFirst = false;
                else
                    yield return (token, tokenStart);
            }
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