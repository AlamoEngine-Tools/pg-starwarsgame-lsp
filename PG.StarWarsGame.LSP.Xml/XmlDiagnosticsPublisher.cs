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
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlDiagnosticsPublisher
{
    private readonly IXmlDocumentFactProducer _documentProducer;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly IXmlDiagnosticsHandlerRegistry _handlerRegistry;
    private readonly IXmlIndexFactProducer _indexProducer;
    private readonly ILogger<XmlDiagnosticsPublisher> _logger;
    private readonly Action<PublishDiagnosticsParams> _publish;
    private readonly ISchemaProvider _schema;
    private readonly IStoryFactProducer _storyProducer;
    private readonly IGameWorkspaceHost _workspaceHost;

    private HashSet<string> _lastPublishedUris = [];

    public XmlDiagnosticsPublisher(
        ILanguageServerFacade server,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IXmlDiagnosticsHandlerRegistry handlerRegistry,
        IXmlDocumentFactProducer documentProducer,
        IXmlIndexFactProducer indexProducer,
        IStoryFactProducer storyProducer,
        ILogger<XmlDiagnosticsPublisher> logger,
        IFileTypeRegistry fileTypeRegistry,
        IFileHelper fileHelper)
        : this(p => server.TextDocument.PublishDiagnostics(p), indexService, workspaceHost,
            schema, handlerRegistry, documentProducer, indexProducer, storyProducer, logger,
            fileTypeRegistry, fileHelper)
    {
    }

    internal XmlDiagnosticsPublisher(
        Action<PublishDiagnosticsParams> publish,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IXmlDiagnosticsHandlerRegistry handlerRegistry,
        IXmlDocumentFactProducer documentProducer,
        IXmlIndexFactProducer indexProducer,
        IStoryFactProducer storyProducer,
        ILogger<XmlDiagnosticsPublisher> logger,
        IFileTypeRegistry fileTypeRegistry,
        IFileHelper fileHelper)
    {
        _publish = publish;
        _workspaceHost = workspaceHost;
        _schema = schema;
        _handlerRegistry = handlerRegistry;
        _documentProducer = documentProducer;
        _indexProducer = indexProducer;
        _storyProducer = storyProducer;
        _logger = logger;
        _fileTypeRegistry = fileTypeRegistry;
        _fileHelper = fileHelper;

        indexService.IndexChanged += OnIndexChanged;
    }

    private void OnIndexChanged(GameIndex newIndex)
    {
        _logger.LogInformation(
            "OnIndexChanged fired: {DocCount} document(s), {DefCount} definition(s), {RefCount} reference(s)",
            newIndex.Documents.Count, newIndex.WorkspaceDefinitions.Count, newIndex.WorkspaceReferences.Count);

        // Iterate open documents from the workspace host so we publish only for editor-open
        // files. The workspace host stores raw LSP URIs (potentially mixed case on Windows);
        // the index stores canonical lowercase URIs. Normalize before index lookups.
        var openDocs = _workspaceHost.All.ToList();
        var openUris = new HashSet<string>(openDocs.Select(d => d.Uri));

        foreach (var doc in openDocs)
        {
            var uri = doc.Uri;
            var canonicalUri = _fileHelper.NormalizeUri(uri);
            var ctx = new DiagnosticsContext(_schema, newIndex, canonicalUri, "en");

            var facts = new List<XmlFact>();
            facts.AddRange(_documentProducer.Produce(doc.Text, uri));
            facts.AddRange(_indexProducer.Produce(canonicalUri, newIndex));
            if (IsStoryParserDocument(uri))
                facts.AddRange(_storyProducer.Produce(doc.Text, uri));

            var allDiags = new List<Diagnostic>();
            foreach (var fact in facts)
            foreach (var result in _handlerRegistry.Dispatch(fact, ctx))
                allDiags.Add(ToLspDiagnostic(fact, result));

            allDiags.AddRange(CollectEnumBoundaryDiagnostics(uri, doc.Text, newIndex));
            allDiags.AddRange(CollectHardcodedRefDiagnostics(uri, doc.Text, newIndex));

            var publishUri = DocumentUri.From(uri);
            _publish(new PublishDiagnosticsParams
            {
                Uri = publishUri,
                Diagnostics = new Container<Diagnostic>(allDiags)
            });
        }

        // Clear diagnostics for files that are no longer open in the editor.
        foreach (var uri in _lastPublishedUris)
            if (!openUris.Contains(uri))
                _publish(new PublishDiagnosticsParams
                {
                    Uri = DocumentUri.From(uri),
                    Diagnostics = new Container<Diagnostic>()
                });

        _lastPublishedUris = openUris;
    }

    internal IReadOnlyList<Diagnostic> CollectEnumBoundaryDiagnostics(
        string documentUri, string text, GameIndex index)
    {
        var hardcoded = index.Baseline.HardcodedEnumValues;
        if (hardcoded.IsEmpty) return [];
        var uriPath = _fileHelper.NormalizeUri(documentUri);
        if (!Path.GetFileName(uriPath).Equals("gameconstants.xml", StringComparison.OrdinalIgnoreCase)) return [];

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
                    Source = AppProperties.LspServerId
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
        WalkForHardcodedRefs(doc.DocumentNode, text, diagnostics);
        return diagnostics;
    }

    private void WalkForHardcodedRefs(HtmlNode node, string text, List<Diagnostic> diagnostics)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;

            var tagDef = _schema.GetTag(child.Name);
            if (tagDef is { ReferenceKind: ReferenceKind.HardcodedSet, HardcodedSet: not null })
                EmitHardcodedRefDiagnostics(child, tagDef, tagDef.HardcodedSet, text, diagnostics);

            WalkForHardcodedRefs(child, text, diagnostics);
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
                if (innerText[j] == '\n')
                    newlineCount++;
            var line0 = child.Line - 1 + newlineCount;
            var col0 = absPos - lineStart;

            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message =
                    $"'{token}' is not a known {tagDef.HardcodedSet?.Name}. Check the schema for valid names.",
                Range = new Range(
                    new Position(line0, col0),
                    new Position(line0, col0 + token.Length)),
                Source = AppProperties.LspServerId
            });
        }
    }

    private static bool IsBoundaryComment(string commentText)
    {
        return commentText.Contains("ABOVE this point", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsStoryParserDocument(string documentUri)
    {
        return _fileTypeRegistry.GetTypesForFile(_fileHelper.NormalizeUri(documentUri)).Contains("StoryParser");
    }

    private static Diagnostic ToLspDiagnostic(XmlFact fact, XmlDiagnosticResult result)
    {
        var line = result.OverrideLine ?? fact.Line;
        var col = result.OverrideColumn ?? fact.Column;
        var length = result.OverrideLength ?? fact.Length;
        return new Diagnostic
        {
            Severity = MapSeverity(result.Severity),
            Message = result.Message,
            Range = new Range(new Position(line, col), new Position(line, col + length)),
            Source = AppProperties.LspServerId
        };
    }

    private static DiagnosticSeverity? MapSeverity(XmlDiagnosticSeverity severity)
    {
        return severity switch
        {
            XmlDiagnosticSeverity.Error => DiagnosticSeverity.Error,
            XmlDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
            XmlDiagnosticSeverity.Information => DiagnosticSeverity.Information,
            XmlDiagnosticSeverity.Hint => DiagnosticSeverity.Hint,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
        };
    }
}