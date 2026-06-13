// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlDiagnosticsPublisher : DiagnosticsPublisherBase, IXmlDiagnosticsRevalidator, IXmlFixCache
{
    private readonly IXmlDocumentFactProducer _documentProducer;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;

    private readonly ConcurrentDictionary<string, Dictionary<(int Line, int Char), string>> _fixCache = new();
    private readonly IXmlDiagnosticsHandlerRegistry _handlerRegistry;
    private readonly IXmlIndexFactProducer _indexProducer;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlDiagnosticsPublisher> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IStoryFactProducer _storyProducer;
    private readonly IXmlVariantFactProducer? _variantProducer;
    private readonly IGameWorkspaceHost _workspaceHost;

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
        IFileHelper fileHelper,
        IXmlVariantFactProducer variantProducer,
        ServerOptions? options = null)
        : this(p => server.TextDocument.PublishDiagnostics(p), indexService, workspaceHost,
            schema, handlerRegistry, documentProducer, indexProducer, storyProducer, logger,
            fileTypeRegistry, fileHelper,
            (int)(options ?? ServerOptions.Default).DiagnosticsDebounce.TotalMilliseconds,
            variantProducer)
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
        IFileHelper fileHelper,
        int debounceMs = 0,
        IXmlVariantFactProducer? variantProducer = null)
        : base(publish, indexService, workspaceHost, debounceMs)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _schema = schema;
        _handlerRegistry = handlerRegistry;
        _documentProducer = documentProducer;
        _indexProducer = indexProducer;
        _storyProducer = storyProducer;
        _logger = logger;
        _fileTypeRegistry = fileTypeRegistry;
        _fileHelper = fileHelper;
        _variantProducer = variantProducer;
    }

    protected override string FileExtension => ".xml";

    public async Task RevalidateWorkspaceAsync(CancellationToken ct)
    {
        ClearAllPublished();
        var index = _indexService.Current;
        foreach (var uri in index.Documents.Keys)
            await RevalidateDocumentAsync(uri, ct);
    }

    public async Task RevalidateDocumentAsync(string uri, CancellationToken ct)
    {
        var index = _indexService.Current;
        string text;
        if (_workspaceHost.TryGet(uri, out var doc))
        {
            text = doc.Text;
        }
        else
        {
            var path = _fileHelper.FileUriToPath(uri);
            if (path is null) return;
            text = await _fileHelper.FileSystem.File.ReadAllTextAsync(path, ct);
        }

        PublishForDocument(uri, text, index);
    }

    public string? GetSuggestedFix(string uri, int startLine, int startChar)
    {
        var key = _fileHelper.NormalizeUri(uri);
        if (_fixCache.TryGetValue(key, out var fixes) &&
            fixes.TryGetValue((startLine, startChar), out var fix))
            return fix;
        return null;
    }

    protected override void PublishForDocument(string uri, string text, GameIndex index)
    {
        var canonicalUri = _fileHelper.NormalizeUri(uri);
        var ctx = new DiagnosticsContext(_schema, index, canonicalUri, "en");

        var facts = new List<XmlFact>();
        facts.AddRange(_documentProducer.Produce(text, uri));
        facts.AddRange(_indexProducer.Produce(canonicalUri, index));
        if (_variantProducer is not null)
            facts.AddRange(_variantProducer.Produce(canonicalUri, text, index));
        if (IsStoryParserDocument(uri))
            facts.AddRange(_storyProducer.Produce(text, uri));

        var allDiags = new List<Diagnostic>();
        foreach (var fact in facts)
        foreach (var result in _handlerRegistry.Dispatch(fact, ctx))
            allDiags.Add(ToLspDiagnostic(fact, result));

        allDiags.AddRange(CollectEnumBoundaryDiagnostics(uri, text, index));
        allDiags.AddRange(CollectHardcodedRefDiagnostics(uri, text, index));

        var normalizedUri = _fileHelper.NormalizeUri(uri);
        var fixes = new Dictionary<(int, int), string>();
        foreach (var d in allDiags)
        {
            var fixToken = d.Data?["fix"]?.Value<string>();
            if (fixToken is not null)
                fixes[(d.Range.Start.Line, d.Range.Start.Character)] = fixToken;
        }

        _fixCache[normalizedUri] = fixes;

        Publish(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = new Container<Diagnostic>(allDiags)
        });
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
        var groups = tagDef.ValueGroups;
        var validNames = new HashSet<string>(
            groups.Count == 0
                ? set.Values.Select(v => v.Name)
                : set.Values
                    .Where(v => v.Groups.Count == 0 ||
                                v.Groups.Any(g => groups.Contains(g, StringComparer.OrdinalIgnoreCase)))
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
            var (line0, col0) = XmlUtility.OffsetToPosition(text, absPos);

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
            Source = AppProperties.LspServerId,
            Data = BuildDiagnosticData(result)
        };
    }

    private static JToken? BuildDiagnosticData(XmlDiagnosticResult result)
    {
        if (result.SuggestedFix is null && result.CreateLocalisationKey is null &&
            result.SquadronSyncJson is null)
            return null;

        var obj = new JObject();
        if (result.SuggestedFix is not null)
            obj["fix"] = result.SuggestedFix;
        if (result.CreateLocalisationKey is not null)
            obj["createLocKey"] = result.CreateLocalisationKey;
        if (result.SquadronSyncJson is not null)
            obj["squadronSync"] = JToken.Parse(result.SquadronSyncJson);
        return obj;
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