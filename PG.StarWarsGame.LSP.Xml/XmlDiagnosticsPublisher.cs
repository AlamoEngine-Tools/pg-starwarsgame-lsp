// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
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
    private readonly IXmlLayerShadowFactProducer? _shadowProducer;
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
        IXmlLayerShadowFactProducer shadowProducer,
        ServerOptions? options = null)
        : this(p => server.TextDocument.PublishDiagnostics(p), indexService, workspaceHost,
            schema, handlerRegistry, documentProducer, indexProducer, storyProducer, logger,
            fileTypeRegistry, fileHelper,
            (int)(options ?? ServerOptions.Default).DiagnosticsDebounce.TotalMilliseconds,
            variantProducer, shadowProducer)
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
        IXmlVariantFactProducer? variantProducer = null,
        IXmlLayerShadowFactProducer? shadowProducer = null)
        : base(publish, indexService, workspaceHost, debounceMs, logger)
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
        _shadowProducer = shadowProducer;
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
        if (_shadowProducer is not null)
            facts.AddRange(_shadowProducer.Produce(canonicalUri, text, index));
        if (IsStoryParserDocument(uri))
            facts.AddRange(_storyProducer.Produce(text, uri));

        var lines = text.Split('\n');
        var allDiags = new List<Diagnostic>();
        foreach (var fact in facts)
        {
            if (fact is XmlSymbolFact symbolFact && IsDuplicateSymbolSuppressed(lines, symbolFact.Line))
                continue;
            foreach (var result in _handlerRegistry.Dispatch(fact, ctx))
                allDiags.Add(ToLspDiagnostic(fact, result));
        }

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

    internal IReadOnlyList<Diagnostic> CollectHardcodedRefDiagnostics(
        string documentUri, string text, GameIndex index)
    {
        var hardcodedSets = _schema.AllHardcodedSets;
        if (hardcodedSets.Count == 0) return [];

        // Skip the expensive HAP re-parse for file types whose schema has no hardcoded-ref tags.
        // Files with unknown/unregistered types fall through to the full walk (defensive).
        var normalizedUri = _fileHelper.NormalizeUri(documentUri);
        var fileTypes = _fileTypeRegistry.GetTypesForFile(normalizedUri);
        if (!fileTypes.IsDefaultOrEmpty &&
            !fileTypes.Any(t => _schema.GetTagsForType(t)
                                       .Any(tag => tag.ReferenceKind == ReferenceKind.HardcodedSet)))
            return [];

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
        var applicableValues = groups.Count == 0
            ? set.Values
            : set.Values
                .Where(v => v.Groups.Count == 0 ||
                            v.Groups.Any(g => groups.Contains(g, StringComparer.OrdinalIgnoreCase)));
        var validNames = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in applicableValues)
            validNames[value.Name] = value.Deprecated;

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
            if (token.Length == 0) continue;
            if (!validNames.TryGetValue(token, out var deprecated))
            {
                var absPos = child.InnerStartIndex + tokenStart;
                var (line0, col0) = XmlUtility.OffsetToPosition(text, absPos);

                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message =
                        $"'{token}' is not a known {tagDef.HardcodedSet?.Name}. Check the schema for valid names.",
                    Range = SafeRange(line0, col0, token.Length),
                    Source = AppProperties.LspServerId
                });
            }
            else if (deprecated)
            {
                var absPos = child.InnerStartIndex + tokenStart;
                var (line0, col0) = XmlUtility.OffsetToPosition(text, absPos);

                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message = $"'{token}' is deprecated.",
                    Range = SafeRange(line0, col0, token.Length),
                    Source = AppProperties.LspServerId
                });
            }
        }
    }

    // Scans up to 5 lines before the symbol's line for a `<!-- lsp:suppress duplicate-symbol -->`
    // annotation. Returns true when found, indicating the duplicate diagnostic should be suppressed.
    private static bool IsDuplicateSymbolSuppressed(string[] lines, int symbolLine0)
    {
        var start = Math.Max(0, symbolLine0 - 5);
        var end = Math.Min(symbolLine0, lines.Length - 1);
        for (var i = start; i <= end; i++)
        {
            if (lines[i].Contains("lsp:suppress duplicate-symbol", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
            Severity = result.Severity.ToLsp(),
            Message = result.Message,
            Range = SafeRange(line, col, length),
            Source = AppProperties.LspServerId,
            Tags = MapTags(result.Tags),
            Data = BuildDiagnosticData(result)
        };
    }

    private static Container<DiagnosticTag>? MapTags(IReadOnlyList<XmlDiagnosticTag>? tags)
    {
        if (tags is null || tags.Count == 0)
            return null;

        return new Container<DiagnosticTag>(tags.Select(t => t switch
        {
            XmlDiagnosticTag.Unnecessary => DiagnosticTag.Unnecessary,
            XmlDiagnosticTag.Deprecated => DiagnosticTag.Deprecated,
            _ => throw new ArgumentOutOfRangeException(nameof(tags), t, null)
        }));
    }

    // LSP positions must be non-negative; a stray negative line/column (e.g. from an HtmlAgilityPack
    // index of -1) would crash the client's diagnostic conversion, so clamp every published range.
    private static Range SafeRange(int line, int col, int length)
    {
        line = Math.Max(0, line);
        col = Math.Max(0, col);
        var end = Math.Max(col, col + length);
        return new Range(new Position(line, col), new Position(line, end));
    }

    private static JToken? BuildDiagnosticData(XmlDiagnosticResult result)
    {
        if (result.SuggestedFix is null && result.CreateLocalisationKey is null &&
            result.SquadronSyncJson is null && !result.RemoveRedundantOverride)
            return null;

        var obj = new JObject();
        if (result.SuggestedFix is not null)
            obj["fix"] = result.SuggestedFix;
        if (result.CreateLocalisationKey is not null)
            obj["createLocKey"] = result.CreateLocalisationKey;
        if (result.SquadronSyncJson is not null)
            obj["squadronSync"] = JToken.Parse(result.SquadronSyncJson);
        if (result.RemoveRedundantOverride)
            obj["removeRedundantOverride"] = true;
        return obj;
    }
}