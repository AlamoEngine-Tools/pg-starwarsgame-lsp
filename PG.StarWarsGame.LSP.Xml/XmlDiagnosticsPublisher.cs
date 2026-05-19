// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Validation;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Validation;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlDiagnosticsPublisher
{
    private readonly ILogger<XmlDiagnosticsPublisher> _logger;
    private readonly Action<PublishDiagnosticsParams> _publish;
    private readonly ISchemaProvider _schema;
    private readonly StoryParserDiagnosticCollector _storyCollector;
    private readonly IXmlValueValidatorRegistry _validators;
    private readonly IGameWorkspaceHost _workspaceHost;

    private HashSet<string> _lastPublishedUris = [];

    public XmlDiagnosticsPublisher(
        ILanguageServerFacade server,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IXmlValueValidatorRegistry validators,
        StoryParserDiagnosticCollector storyCollector,
        ILogger<XmlDiagnosticsPublisher> logger)
        : this(p => server.TextDocument.PublishDiagnostics(p), indexService, workspaceHost,
            schema, validators, storyCollector, logger)
    {
    }

    internal XmlDiagnosticsPublisher(
        Action<PublishDiagnosticsParams> publish,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IXmlValueValidatorRegistry validators,
        StoryParserDiagnosticCollector storyCollector,
        ILogger<XmlDiagnosticsPublisher> logger)
    {
        _publish = publish;
        _workspaceHost = workspaceHost;
        _schema = schema;
        _validators = validators;
        _storyCollector = storyCollector;
        _logger = logger;

        indexService.IndexChanged += OnIndexChanged;
    }

    private void OnIndexChanged(GameIndex newIndex)
    {
        var currentUris = new HashSet<string>(newIndex.Documents.Keys);

        foreach (var uri in currentUris)
        {
            var allDiags = new List<Diagnostic>();

            if (_workspaceHost.TryGet(uri, out var doc))
            {
                allDiags.AddRange(CollectDiagnostics(doc.Text, DocumentUri.From(uri)));
                allDiags.AddRange(CollectEnumBoundaryDiagnostics(uri, doc.Text, newIndex));
                allDiags.AddRange(CollectHardcodedRefDiagnostics(uri, doc.Text, newIndex));
                if (IsStoryParserDocument(uri, newIndex))
                    allDiags.AddRange(_storyCollector.Collect(doc.Text, newIndex));
            }

            allDiags.AddRange(CollectDuplicateIdDiagnostics(uri, newIndex));
            allDiags.AddRange(CollectUnresolvedRefDiagnostics(uri, newIndex));

            _publish(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.From(uri),
                Diagnostics = new Container<Diagnostic>(allDiags)
            });
        }

        // Clear diagnostics for URIs that are no longer in the index.
        foreach (var uri in _lastPublishedUris)
            if (!currentUris.Contains(uri))
                _publish(new PublishDiagnosticsParams
                {
                    Uri = DocumentUri.From(uri),
                    Diagnostics = new Container<Diagnostic>()
                });

        _lastPublishedUris = currentUris;
    }

    internal IReadOnlyList<Diagnostic> CollectDuplicateIdDiagnostics(string documentUri, GameIndex index)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var (id, symbols) in index.WorkspaceDefinitions)
        {
            if (symbols.Length <= 1) continue;
            foreach (var sym in symbols)
            {
                if (sym.Origin is not FileOrigin fo || fo.Uri != documentUri) continue;

                var otherUris = symbols
                    .Where(s => !ReferenceEquals(s, sym))
                    .Select(s => s.Origin is FileOrigin f ? f.Uri : "unknown")
                    .Distinct()
                    .ToList();
                var othersText = otherUris.Count == 1
                    ? $" Also defined in: {otherUris[0]}."
                    : $" Also defined in: {string.Join(", ", otherUris)}.";

                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = $"Duplicate object ID '{id}': IDs must be globally unique.{othersText}",
                    Range = new Range(new Position(fo.Line, 0), new Position(fo.Line, int.MaxValue)),
                    Source = "pg-swg-lsp"
                });
            }
        }

        return diagnostics;
    }

    internal IReadOnlyList<Diagnostic> CollectUnresolvedRefDiagnostics(string documentUri, GameIndex index)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var (targetId, refs) in index.WorkspaceReferences)
        {
            if (index.Resolve(targetId) is not null) continue;
            foreach (var r in refs)
            {
                if (r.DocumentUri != documentUri) continue;
                var msg = r.ExpectedTypeName is not null
                    ? $"Unresolved reference '{targetId}' (expected {r.ExpectedTypeName})."
                    : $"Unresolved reference '{targetId}'.";
                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = msg,
                    Range = new Range(new Position(r.Line, r.Column), new Position(r.Line, r.Column + r.Length)),
                    Source = "pg-swg-lsp"
                });
            }
        }

        return diagnostics;
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

    internal IReadOnlyList<Diagnostic> CollectEnumBoundaryDiagnostics(
        string documentUri, string text, GameIndex index)
    {
        var hardcoded = index.Baseline.HardcodedEnumValues;
        if (hardcoded.IsEmpty) return [];
        if (!documentUri.EndsWith("GameConstants.xml", StringComparison.OrdinalIgnoreCase)) return [];

        XDocument doc;
        try
        {
            doc = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch
        {
            return [];
        }

        var diagnostics = new List<Diagnostic>();

        foreach (var (enumName, tagName) in
                 (IEnumerable<(string, string)>)[("DamageType", "Damage_Types"), ("ArmorType", "Armor_Types")])
        {
            if (!hardcoded.TryGetValue(enumName, out var knownHardcoded)) continue;
            var knownSet = new HashSet<string>(knownHardcoded, StringComparer.OrdinalIgnoreCase);

            var el = doc.Descendants(tagName).FirstOrDefault();
            if (el is null) continue;

            var pastBoundary = false;
            foreach (var node in el.Nodes())
            {
                if (node is XComment c && IsBoundaryComment(c.Value))
                {
                    pastBoundary = true;
                    continue;
                }

                if (!pastBoundary || node is not XText textNode) continue;

                var li = (IXmlLineInfo)textNode;
                EmitTokenDiagnostics(textNode.Value, li.LineNumber, li.LinePosition,
                    knownSet, diagnostics);
            }
        }

        return diagnostics;
    }

    private void EmitTokenDiagnostics(
        string raw, int startLine, int startCol,
        HashSet<string> knownSet, List<Diagnostic> diagnostics)
    {
        var lines = raw.Split('\n');
        for (var li = 0; li < lines.Length; li++)
        {
            var line = lines[li].TrimEnd('\r');
            var lineNumber = startLine + li;
            var col = 0;

            while (col < line.Length)
            {
                while (col < line.Length && char.IsWhiteSpace(line[col])) col++;
                if (col >= line.Length) break;

                var tokenStart = col;
                while (col < line.Length && !char.IsWhiteSpace(line[col])) col++;
                var token = line[tokenStart..col];

                if (knownSet.Contains(token)) continue;

                // 0-based column: first line of text node starts at startCol-1, subsequent at 0
                var tokenCol0 = (li == 0 ? startCol - 1 : 0) + tokenStart;
                var tokenLine0 = lineNumber - 1;
                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message =
                        $"'{token}' is below the hard-coded section boundary. Add new damage/armor types above the boundary comment.",
                    Range = new Range(
                        new Position(tokenLine0, tokenCol0),
                        new Position(tokenLine0, tokenCol0 + token.Length)),
                    Source = "pg-swg-lsp"
                });
            }
        }
    }

    internal IReadOnlyList<Diagnostic> CollectHardcodedRefDiagnostics(
        string documentUri, string text, GameIndex index)
    {
        var hardcodedSets = _schema.AllHardcodedSets;
        if (hardcodedSets.Count == 0) return [];

        var diagnostics = new List<Diagnostic>();
        var doc = new HtmlDocument();
        doc.LoadHtml(text);
        WalkForHardcodedRefs(doc.DocumentNode, text, hardcodedSets, diagnostics);
        return diagnostics;
    }

    private void WalkForHardcodedRefs(
        HtmlNode node, string text,
        IReadOnlyList<HardcodedReferenceSet> hardcodedSets,
        List<Diagnostic> diagnostics)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;

            var tagDef = _schema.GetTag(child.Name);
            if (tagDef?.ReferenceKind == ReferenceKind.HardcodedSet && tagDef.ReferenceType is not null)
            {
                var set = hardcodedSets.FirstOrDefault(s =>
                    string.Equals(s.Name, tagDef.ReferenceType, StringComparison.OrdinalIgnoreCase));
                if (set is not null)
                    EmitHardcodedRefDiagnostics(child, tagDef, set, text, diagnostics);
            }

            WalkForHardcodedRefs(child, text, hardcodedSets, diagnostics);
        }
    }

    private static void EmitHardcodedRefDiagnostics(
        HtmlNode child, XmlTagDefinition tagDef, HardcodedReferenceSet set,
        string text, List<Diagnostic> diagnostics)
    {
        var validNames = new HashSet<string>(
            tagDef.ValueGroup is null
                ? set.Values.Select(v => v.Name)
                : set.Values
                    .Where(v => v.Groups.Count == 0 ||
                                v.Groups.Any(g => string.Equals(g, tagDef.ValueGroup,
                                    StringComparison.OrdinalIgnoreCase)))
                    .Select(v => v.Name),
            StringComparer.OrdinalIgnoreCase);

        var innerText = child.InnerText;
        char[] separators = [',', ' ', '\t', '\r', '\n'];

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
            if (token.Length == 0 || validNames.Contains(token)) continue;

            var absPos = child.InnerStartIndex + tokenStart;
            var lineStart = text.LastIndexOf('\n', Math.Max(0, absPos - 1)) + 1;
            var newlineCount = 0;
            for (var j = 0; j < tokenStart && j < innerText.Length; j++)
                if (innerText[j] == '\n') newlineCount++;
            var line0 = child.Line - 1 + newlineCount;
            var col0 = absPos - lineStart;

            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message =
                    $"'{token}' is not a known {tagDef.ReferenceType}. Check the schema for valid names.",
                Range = new Range(
                    new Position(line0, col0),
                    new Position(line0, col0 + token.Length)),
                Source = "pg-swg-lsp"
            });
        }
    }

    private static bool IsBoundaryComment(string commentText)
    {
        return commentText.Contains("ABOVE this point", StringComparison.OrdinalIgnoreCase);
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
            Severity = MapSeverity(result.Severity),
            Message = result.Message,
            Range = new Range(
                new Position(startLine, startCol),
                new Position(endLine, endCol)),
            Source = "pg-swg-lsp"
        };
    }

    private static bool IsStoryParserDocument(string uri, GameIndex index)
    {
        return index.Documents.TryGetValue(uri, out var doc) &&
               doc.Symbols.Any(s => string.Equals(s.TypeName, "StoryParser", StringComparison.OrdinalIgnoreCase));
    }

    private static DiagnosticSeverity? MapSeverity(XmlValidationSeverity resultSeverity)
    {
        return resultSeverity switch
        {
            XmlValidationSeverity.Error => DiagnosticSeverity.Error,
            XmlValidationSeverity.Warning => DiagnosticSeverity.Warning,
            XmlValidationSeverity.Information => DiagnosticSeverity.Information,
            XmlValidationSeverity.Hint => DiagnosticSeverity.Hint,
            _ => throw new ArgumentOutOfRangeException(nameof(resultSeverity), resultSeverity, null)
        };
    }
}