// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class XmlDiagnosticsPublisher : DiagnosticsPublisherBase, IXmlDiagnosticsRevalidator, IXmlFixCache,
    IXmlDiagnosticsCollector
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
    private readonly IXmlParseCache _parseCache;
    private readonly IStoryChainProblemStore? _storyChainProblems;
    private readonly IStoryGraphDiagnosticsSource? _storyGraphDiagnostics;
    private readonly IStoryFactProducer _storyProducer;
    private readonly IDocumentTextSource _textSource;
    private readonly IXmlVariantFactProducer? _variantProducer;
    private readonly IGameWorkspaceHost _workspaceHost;
    private readonly ILspConfigurationProvider? _configProvider;

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
        IDocumentTextSource textSource,
        IXmlParseCache parseCache,
        ILspConfigurationProvider configProvider,
        IStoryChainProblemStore storyChainProblems,
        IStoryGraphDiagnosticsSource storyGraphDiagnostics,
        ServerOptions? options = null)
        : this(p => server.TextDocument.PublishDiagnostics(p), indexService, workspaceHost,
            schema, handlerRegistry, documentProducer, indexProducer, storyProducer, logger,
            fileTypeRegistry, fileHelper,
            (int)(options ?? ServerOptions.Default).DiagnosticsDebounce.TotalMilliseconds,
            variantProducer, shadowProducer, textSource, parseCache, configProvider, storyChainProblems,
            storyGraphDiagnostics)
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
        IXmlLayerShadowFactProducer? shadowProducer = null,
        IDocumentTextSource? textSource = null,
        IXmlParseCache? parseCache = null,
        ILspConfigurationProvider? configProvider = null,
        IStoryChainProblemStore? storyChainProblems = null,
        IStoryGraphDiagnosticsSource? storyGraphDiagnostics = null)
        : base(publish, indexService, workspaceHost, debounceMs, logger)
    {
        _configProvider = configProvider;
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _textSource = textSource ?? new DocumentTextSource(workspaceHost, fileHelper,
            NullLogger<DocumentTextSource>.Instance);
        _parseCache = parseCache ?? new XmlParseCache(_textSource, ServerOptions.Default.ParseCacheCapacity);
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
        _storyChainProblems = storyChainProblems;
        _storyGraphDiagnostics = storyGraphDiagnostics;
    }

    protected override string FileExtension => ".xml";

    // Feature-flag gate: a null provider (test convenience ctors) means always enabled.
    protected override bool DiagnosticsEnabled =>
        _configProvider?.Current.Features.Xml.Diagnostics ?? true;

    public async Task RevalidateWorkspaceAsync(CancellationToken ct)
    {
        if (!DiagnosticsEnabled) return;

        ClearAllPublished();
        var index = _indexService.Current;
        foreach (var uri in index.Documents.Keys)
            await RevalidateDocumentAsync(uri, ct);
    }

    public Task RevalidateDocumentAsync(string uri, CancellationToken ct)
    {
        if (!DiagnosticsEnabled) return Task.CompletedTask;

        var index = _indexService.Current;
        var text = _textSource.GetText(_fileHelper.NormalizeUri(uri))?.Text;
        if (text is null) return Task.CompletedTask;

        PublishForDocument(uri, text, index);
        return Task.CompletedTask;
    }

    public string? GetSuggestedFix(string uri, int startLine, int startChar)
    {
        var key = _fileHelper.NormalizeUri(uri);
        if (_fixCache.TryGetValue(key, out var fixes) &&
            fixes.TryGetValue((startLine, startChar), out var fix))
            return fix;
        return null;
    }

    /// <summary>
    ///     The full pipeline for one document, publish-free (<see cref="IXmlDiagnosticsCollector" />) —
    ///     the push path below and on-demand consumers (story graph) share this body.
    /// </summary>
    public IReadOnlyList<Diagnostic> Collect(string uri, string text, GameIndex index)
    {
        var canonicalUri = _fileHelper.NormalizeUri(uri);
        var ctx = new DiagnosticsContext(_schema, index, canonicalUri, "en");

        // One parse shared by every producer — and via the parse cache, shared with the indexing
        // parse and every request handler touching the same content.
        var parsed = _parseCache.GetOrParse(canonicalUri, text);

        var facts = new List<XmlFact>();
        facts.AddRange(_documentProducer.Produce(parsed, uri));
        facts.AddRange(_indexProducer.Produce(canonicalUri, index));
        if (_variantProducer is not null)
            facts.AddRange(_variantProducer.Produce(canonicalUri, parsed, index));
        if (_shadowProducer is not null)
            facts.AddRange(_shadowProducer.Produce(canonicalUri, parsed, index));
        if (IsStoryParserDocument(uri))
            facts.AddRange(_storyProducer.Produce(parsed, uri));

        var lines = parsed.Lines;
        var allDiags = new List<Diagnostic>();
        foreach (var fact in facts)
        {
            if (fact is XmlSymbolFact symbolFact && IsDuplicateSymbolSuppressed(lines, symbolFact.Line))
                continue;
            foreach (var result in _handlerRegistry.Dispatch(fact, ctx))
                allDiags.Add(ToLspDiagnostic(fact, result));
        }

        allDiags.AddRange(CollectHardcodedRefDiagnostics(uri, parsed, index));
        allDiags.AddRange(CollectDamageTypeOrderDiagnostics(uri, parsed));
        if (_storyChainProblems is not null)
            allDiags.AddRange(_storyChainProblems.GetForDocument(canonicalUri).Select(ToLspDiagnostic));
        if (_storyGraphDiagnostics is not null && IsStoryParserDocument(uri))
            allDiags.AddRange(_storyGraphDiagnostics.GetForDocument(canonicalUri).Select(ToLspDiagnostic));

        return allDiags;
    }

    protected override void PublishForDocument(string uri, string text, GameIndex index)
    {
        var allDiags = Collect(uri, text, index);

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

    // Test convenience: parses the text itself; the publish path shares one ParsedXmlDocument.
    internal IReadOnlyList<Diagnostic> CollectHardcodedRefDiagnostics(
        string documentUri, string text, GameIndex index)
    {
        return CollectHardcodedRefDiagnostics(documentUri, ParsedXmlDocument.Parse(text), index);
    }

    internal IReadOnlyList<Diagnostic> CollectHardcodedRefDiagnostics(
        string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        var hardcodedSets = _schema.AllHardcodedSets;
        if (hardcodedSets.Count == 0) return [];

        // Skip the tree walk for file types whose schema has no hardcoded-ref tags.
        // Files with unknown/unregistered types fall through to the full walk (defensive).
        var normalizedUri = _fileHelper.NormalizeUri(documentUri);
        var fileTypes = _fileTypeRegistry.GetTypesForFile(normalizedUri);
        if (!fileTypes.IsDefaultOrEmpty &&
            !fileTypes.Any(t => _schema.GetTagsForType(t)
                                       .Any(tag => tag.ReferenceKind == ReferenceKind.HardcodedSet)))
            return [];

        var diagnostics = new List<Diagnostic>();
        WalkForHardcodedRefs(document.Html.DocumentNode, document.LineIndex, diagnostics);
        return diagnostics;
    }

    // The engine hardcodes these 20 damage types at fixed positions relative to the end of
    // GameConstants.xml's <Damage_Types> list. If they aren't present as the exact tail, in this
    // exact order, the game crashes at runtime.
    private static readonly string[] RequiredDamageTypeTail =
    [
        "Damage_Normal", "Damage_Force_Whirlwind", "Damage_Force_Telekinesis", "Damage_Force_Lightning",
        "Damage_Force_Corruption", "Damage_Hard_Point_Self_Destruct", "Damage_Fire", "Damage_Cable_Attack",
        "Damage_Explosion", "Damage_Asteroid", "Damage_Cable_Attack_Deployed", "Damage_Normal_Deployed",
        "Damage_Vehicle_Thief", "Damage_Crush", "Damage_Eat", "Damage_Redirected", "Damage_Wampa",
        "Damage_Infection", "Damage_Remote_Bomb", "Damage_Drain_Life"
    ];

    private static readonly char[] DamageTypeTokenSeparators = [' ', '\t', '\r', '\n', ','];

    // Test convenience: parses the text itself; the publish path shares one ParsedXmlDocument.
    internal IReadOnlyList<Diagnostic> CollectDamageTypeOrderDiagnostics(string documentUri, string text)
    {
        return CollectDamageTypeOrderDiagnostics(documentUri, ParsedXmlDocument.Parse(text));
    }

    internal IReadOnlyList<Diagnostic> CollectDamageTypeOrderDiagnostics(
        string documentUri, ParsedXmlDocument document)
    {
        var text = document.Text;

        // Cheap pre-check before walking the tree — almost no documents declare this element.
        if (!text.Contains("Damage_Types", StringComparison.OrdinalIgnoreCase))
            return [];

        var element = document.Html.DocumentNode.Descendants().FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element && n.Name.Equals("Damage_Types", StringComparison.OrdinalIgnoreCase));
        if (element is null)
            return [];

        var tokens = new List<(string Token, int AbsPos)>();
        foreach (var node in element.ChildNodes)
        {
            if (node.NodeType != HtmlNodeType.Text) continue;
            var nodeText = node.InnerText;
            var nodeStart = node.StreamPosition;
            foreach (var (token, offset) in SplitDamageTypeTokens(nodeText))
                tokens.Add((token, nodeStart + offset));
        }

        var lineIndex = document.LineIndex;
        if (tokens.Count < RequiredDamageTypeTail.Length)
        {
            var (line, col) = tokens.Count > 0
                ? lineIndex.GetPosition(tokens[^1].AbsPos)
                : lineIndex.GetPosition(element.InnerStartIndex);
            var len = tokens.Count > 0 ? tokens[^1].Token.Length : 0;
            return
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message =
                        $"<Damage_Types> has only {tokens.Count} value(s); it must end with the engine's " +
                        $"{RequiredDamageTypeTail.Length} hardcoded damage types, in order, or the game will crash.",
                    Range = SafeRange(line, col, len),
                    Source = AppProperties.LspServerId
                }
            ];
        }

        var tail = tokens.GetRange(tokens.Count - RequiredDamageTypeTail.Length, RequiredDamageTypeTail.Length);
        var diagnostics = new List<Diagnostic>();
        for (var i = 0; i < RequiredDamageTypeTail.Length; i++)
        {
            if (string.Equals(tail[i].Token, RequiredDamageTypeTail[i], StringComparison.Ordinal))
                continue;

            var (line, col) = lineIndex.GetPosition(tail[i].AbsPos);
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message =
                    $"'{tail[i].Token}' must be '{RequiredDamageTypeTail[i]}' — the last " +
                    $"{RequiredDamageTypeTail.Length} entries of <Damage_Types> must exactly match the " +
                    "engine's hardcoded order or the game will crash.",
                Range = SafeRange(line, col, tail[i].Token.Length),
                Source = AppProperties.LspServerId
            });
        }

        return diagnostics;
    }

    private static IEnumerable<(string Token, int Offset)> SplitDamageTypeTokens(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && Array.IndexOf(DamageTypeTokenSeparators, text[i]) >= 0) i++;
            if (i >= text.Length) break;
            var start = i;
            while (i < text.Length && Array.IndexOf(DamageTypeTokenSeparators, text[i]) < 0) i++;
            yield return (text[start..i], start);
        }
    }

    private void WalkForHardcodedRefs(HtmlNode node, LineOffsetIndex lineIndex, List<Diagnostic> diagnostics)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;

            var tagDef = _schema.GetTag(child.Name);
            if (tagDef is { ReferenceKind: ReferenceKind.HardcodedSet, HardcodedSet: not null })
                EmitHardcodedRefDiagnostics(child, tagDef, tagDef.HardcodedSet, lineIndex, diagnostics);

            WalkForHardcodedRefs(child, lineIndex, diagnostics);
        }
    }

    private static void EmitHardcodedRefDiagnostics(
        HtmlNode child, XmlTagDefinition tagDef, HardcodedReferenceSet set,
        LineOffsetIndex lineIndex, List<Diagnostic> diagnostics)
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
                var (line0, col0) = lineIndex.GetPosition(absPos);

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
                var (line0, col0) = lineIndex.GetPosition(absPos);

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

    private static Diagnostic ToLspDiagnostic(StoryChainProblem problem)
    {
        return new Diagnostic
        {
            Severity = problem.Severity.ToLsp(),
            Message = problem.Message,
            Range = SafeRange(problem.Line, problem.Column, problem.EndLine, problem.EndColumn),
            Source = AppProperties.LspServerId,
            Code = "story-chain"
        };
    }

    private static Diagnostic ToLspDiagnostic(StoryGraphDiagnostic diagnostic)
    {
        return new Diagnostic
        {
            Severity = diagnostic.Severity.ToLsp(),
            Message = diagnostic.Message,
            Range = SafeRange(diagnostic.Line, diagnostic.Column, diagnostic.EndLine, diagnostic.EndColumn),
            Source = AppProperties.LspServerId,
            Code = "story-graph"
        };
    }

    private static Diagnostic ToLspDiagnostic(XmlFact fact, XmlDiagnosticResult result)
    {
        var line = result.OverrideLine ?? fact.Line;
        var col = result.OverrideColumn ?? fact.Column;
        var endLine = result.OverrideEndLine ?? fact.EndLine;

        // A cross-line span (fact or result carries an explicit end line) wins over the default
        // same-line col+length range — used e.g. to grey out a whole multi-line element.
        var range = endLine is { } el
            ? SafeRange(line, col, el, result.OverrideEndColumn ?? fact.EndColumn ?? 0)
            : SafeRange(line, col, result.OverrideLength ?? fact.Length);

        return new Diagnostic
        {
            Severity = result.Severity.ToLsp(),
            Message = result.Message,
            Range = range,
            Source = AppProperties.LspServerId,
            Tags = MapTags(result.Tags),
            Data = BuildDiagnosticData(result),
            RelatedInformation = MapRelatedInformation(result.RelatedLocations)
        };
    }

    private static Container<DiagnosticRelatedInformation>? MapRelatedInformation(
        IReadOnlyList<XmlRelatedLocation>? locations)
    {
        if (locations is null || locations.Count == 0)
            return null;

        return new Container<DiagnosticRelatedInformation>(locations.Select(l =>
            new DiagnosticRelatedInformation
            {
                Message = l.Message,
                Location = new Location
                {
                    Uri = l.Uri,
                    Range = SafeRange(l.Line, l.Column ?? 0, 0)
                }
            }));
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

    // Cross-line variant: the end position may legitimately be on a later line (e.g. a whole
    // multi-line element) — clamp endLine to at least startLine so a malformed/negative value can't
    // produce an inverted range.
    private static Range SafeRange(int startLine, int startCol, int endLine, int endCol)
    {
        startLine = Math.Max(0, startLine);
        startCol = Math.Max(0, startCol);
        endLine = Math.Max(startLine, endLine);
        endCol = Math.Max(0, endCol);
        return new Range(new Position(startLine, startCol), new Position(endLine, endCol));
    }

    private static JToken? BuildDiagnosticData(XmlDiagnosticResult result)
    {
        if (result.SuggestedFix is null && result.CreateLocalisationKey is null &&
            result.SquadronSyncJson is null && !result.RemoveRedundantOverride &&
            !result.OfferRemoveEarlierDuplicates)
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
        if (result.OfferRemoveEarlierDuplicates)
            obj["removeEarlierDuplicates"] = true;
        return obj;
    }
}