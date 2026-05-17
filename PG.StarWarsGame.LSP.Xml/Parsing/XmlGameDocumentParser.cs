using System.Collections.Immutable;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Parsing;

public sealed class XmlGameDocumentParser : IGameDocumentParser
{
    private readonly ISchemaProvider _schema;
    private readonly ILogger<XmlGameDocumentParser> _logger;

    public XmlGameDocumentParser(ISchemaProvider schema, ILogger<XmlGameDocumentParser> logger)
    {
        _schema = schema;
        _logger = logger;
    }

    public bool CanParse(string fileExtension) =>
        fileExtension.Equals(".xml", StringComparison.OrdinalIgnoreCase);

    public ValueTask<DocumentIndex> ParseAsync(
        string documentUri, string text, int version, CancellationToken ct)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(text);

        var symbols    = new List<GameSymbol>();
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
                {
                    _logger.LogDebug("Type '{Type}' element at line {Line} has no Name attribute — skipped",
                        typeDef.TypeName, node.Line);
                }
                else
                {
                    // HAP line numbers are 1-based; LSP coordinates are 0-based.
                    symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject, typeDef.TypeName,
                        new FileOrigin(documentUri, node.Line - 1, null), null));
                }
            }

            // Emit references for direct child tags with ReferenceKind.XmlObject.
            foreach (var child in node.ChildNodes
                         .Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var tagDef = _schema.GetTag(child.Name);
                if (tagDef?.ReferenceKind != ReferenceKind.XmlObject) continue;

                var targetId = child.InnerText.Trim();
                if (string.IsNullOrEmpty(targetId)) continue;

                references.Add(new GameReference(
                    targetId,
                    GameSymbolKind.XmlObject,
                    tagDef.ReferenceType,
                    documentUri,
                    child.Line - 1,
                    child.InnerStartIndex,
                    targetId.Length));
            }
        }

        return ValueTask.FromResult(new DocumentIndex(
            documentUri, version,
            symbols.ToImmutableArray(),
            references.ToImmutableArray()));
    }
}
