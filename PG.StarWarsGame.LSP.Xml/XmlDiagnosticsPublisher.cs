using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlDiagnosticsPublisher : IXmlDiagnosticsPublisher
{
    private readonly ILogger<XmlDiagnosticsPublisher> _logger;
    private readonly ISchemaProvider _schema;
    private readonly ILanguageServerFacade _server;
    private readonly IXmlValueValidatorRegistry _validators;

    public XmlDiagnosticsPublisher(
        ILanguageServerFacade server,
        ISchemaProvider schema,
        IXmlValueValidatorRegistry validators,
        ILogger<XmlDiagnosticsPublisher> logger)
    {
        _server = server;
        _schema = schema;
        _validators = validators;
        _logger = logger;
    }

    public void Publish(DocumentUri uri, string text)
    {
        var diagnostics = CollectDiagnostics(text, uri);
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(diagnostics)
        });
    }

    public void ClearDiagnostics(DocumentUri uri)
    {
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>()
        });
    }

    internal List<Diagnostic> CollectDiagnostics(string text, DocumentUri? uri = null)
    {
        var diagnostics = new List<Diagnostic>();
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(text);
            var lines = text.Split('\n');
            // Iterate root elements directly so the file-level container is never treated as a tag.
            foreach (var root in doc.DocumentNode.ChildNodes)
            {
                if (root.NodeType != HtmlNodeType.Element) continue;
                WalkNodes(root, diagnostics, lines);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse XML for diagnostics: {Uri}", uri);
        }

        return diagnostics;
    }

    private void WalkNodes(HtmlNode node, List<Diagnostic> diagnostics, string[] lines)
    {
        // Pass 1: group direct child elements by name
        var childGroups = new Dictionary<string, List<HtmlNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;
            if (!childGroups.TryGetValue(child.Name, out var list))
                childGroups[child.Name] = list = [];
            list.Add(child);
        }

        // Identify singleton tags that appear more than once under this parent.
        // Type containers (e.g. <Faction>) are skipped — multiple instances are always valid.
        var duplicatedSingletons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, nodes) in childGroups)
        {
            if (nodes.Count <= 1) continue;
            if (_schema.GetObjectType(name) is not null) continue;
            var tagDef = _schema.GetTag(name);
            if (tagDef is not null && !tagDef.MultipleAllowed)
                duplicatedSingletons.Add(name);
        }

        // Pass 2: validate each child
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;

            var name = child.Name;

            // Skip type-container elements (e.g. <GameObjectType>, <Faction>)
            if (_schema.GetObjectType(name) is not null)
            {
                WalkNodes(child, diagnostics, lines);
                continue;
            }

            var tagDef = _schema.GetTag(name);
            if (tagDef is null)
            {
                WalkNodes(child, diagnostics, lines);
                continue;
            }

            if (duplicatedSingletons.Contains(name))
            {
                var definedName = tagDef.Tag;
                var otherLines = childGroups[name]
                    .Where(n => !ReferenceEquals(n, child))
                    .Select(n => n.Line.ToString())
                    .ToList();
                var othersText = otherLines.Count == 1
                    ? $" Also at line {otherLines[0]}."
                    : $" Also at lines {string.Join(", ", otherLines)}.";
                diagnostics.Add(BuildDiagnostic(child,
                    XmlValidationResult.Failure(
                        $"Duplicate tag '{definedName}': only one occurrence is allowed per object.{othersText}"),
                    lines, true));
                WalkNodes(child, diagnostics, lines);
                continue;
            }

            var rawValue = child.InnerText.Trim();
            if (string.IsNullOrEmpty(rawValue))
                continue;

            var result = _validators.Validate(tagDef.ValueType, rawValue, tagDef);
            if (!result.IsValid)
                diagnostics.Add(BuildDiagnostic(child, result, lines));

            WalkNodes(child, diagnostics, lines);
        }
    }

    private static Diagnostic BuildDiagnostic(HtmlNode node, XmlValidationResult result, string[] lines,
        bool openingTagOnly = false)
    {
        // HtmlAgilityPack Line is 1-based; LSP is 0-based.
        // LinePosition does not reliably reflect indentation, so find the '<' directly in the source.
        var startLine = Math.Max(0, node.Line - 1);
        var sourceLine = startLine < lines.Length ? lines[startLine].TrimEnd('\r') : string.Empty;
        var startCol = sourceLine.IndexOf('<');
        if (startCol < 0) startCol = 0;

        int endLine, endCol;
        if (openingTagOnly)
        {
            // Highlight only the opening tag bracket, e.g. <Max_Speed> — not the content or closing tag.
            var closeAngle = sourceLine.IndexOf('>', startCol);
            endLine = startLine;
            endCol = closeAngle >= 0 ? closeAngle + 1 : startCol + node.Name.Length + 2;
        }
        else
        {
            // Extend the range to cover the full element. OuterHtml is the complete element text.
            var outer = node.OuterHtml;
            var newlines = outer.Count(c => c == '\n');
            if (newlines == 0)
            {
                endLine = startLine;
                endCol = startCol + outer.Length;
            }
            else
            {
                endLine = startLine + newlines;
                endCol = outer.Length - outer.LastIndexOf('\n') - 1;
            }
        }

        return new Diagnostic
        {
            Severity = result.Severity == XmlValidationSeverity.Warning
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error,
            Message = result.Message,
            Range = new Range(
                new Position(startLine, startCol),
                new Position(endLine, endCol)),
            Source = "pg-swg-lsp"
        };
    }
}