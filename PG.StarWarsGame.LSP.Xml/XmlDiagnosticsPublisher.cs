// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Diagnostics;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Suppression;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlDiagnosticsPublisher : DiagnosticsPublisherBase, IXmlDiagnosticsRevalidator, IXmlFixCache,
    ICacheStatisticsSource,
    IXmlDiagnosticsCollector
{
    /// <summary>
    ///     How long one document may take to diagnose before it is worth naming in the log. Well
    ///     above any healthy document (the median is a couple of milliseconds) so the line only
    ///     appears for the handful that actually dominate a sweep.
    /// </summary>
    private const long SlowDocumentMs = 1000;

    private readonly ILspConfigurationProvider _configProvider;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;

    private readonly ConcurrentDictionary<string, Dictionary<(int Line, int Char), string>> _fixCache = new();

    private readonly IXmlDiagnosticsHandlerRegistry _handlerRegistry;
    private readonly IIconNameIndex _iconNames;
    private readonly IIconRepackStatusProvider _iconRepack;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlDiagnosticsPublisher> _logger;
    private readonly IMessageCatalog _messages;
    private readonly IModelTextureIndex _modelTextures;
    private readonly IXmlParseCache _parseCache;

    /// <summary>
    ///     Every fact source, in registration order - which is the order the facts, and so the
    ///     diagnostics, come out in. The publisher knows none of them by name (#307).
    /// </summary>
    private readonly IReadOnlyList<IXmlFactProducer> _producers;

    private readonly ISchemaProvider _schema;
    private readonly IStoryChainProblemStore _storyChainProblems;
    private readonly IStoryGraphDiagnosticsSource _storyGraphDiagnostics;
    private readonly IDocumentTextSource _textSource;

    /// <summary>
    ///     The workspace half of variant resolution. Held rather than a finished resolver because a
    ///     resolver is bound to one <see cref="GameIndex" />, and the index changes between runs.
    /// </summary>
    private readonly IVariantTagSource _variantTagSource;

    // Every dependency is REQUIRED, deliberately, and there is one constructor. An optional
    // parameter was the one shape the container was free to skip, and skipping it is silent: the
    // tag source gone, every cross-object rule reports nothing forever; the suppression store gone,
    // a globally suppressed diagnostic keeps publishing (MEASURED 2026-10-04, through a second
    // constructor that never forwarded it); the layer map gone, every source tag reads bare. The
    // graph now either supplies each of them or fails at startup, where the registration test and
    // the server launch both see it (#305).
    public XmlDiagnosticsPublisher(
        IDiagnosticsSink sink,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IXmlDiagnosticsHandlerRegistry handlerRegistry,
        IEnumerable<IXmlFactProducer> producers,
        ILogger<XmlDiagnosticsPublisher> logger,
        IFileTypeRegistry fileTypeRegistry,
        IFileHelper fileHelper,
        IDocumentTextSource textSource,
        IXmlParseCache parseCache,
        ILspConfigurationProvider configProvider,
        IStoryChainProblemStore storyChainProblems,
        IStoryGraphDiagnosticsSource storyGraphDiagnostics,
        IVariantTagSource variantTagSource,
        IGlobalSuppressionStore globalSuppressions,
        IProjectLayerMap layerMap,
        ServerOptions options,
        IIconRepackStatusProvider iconRepack,
        IModelTextureIndex modelTextures,
        IIconNameIndex iconNames,
        IMessageCatalog messages)
        : base(sink, indexService, workspaceHost, options, logger, globalSuppressions, layerMap)
    {
        _iconRepack = iconRepack;
        _modelTextures = modelTextures;
        _iconNames = iconNames;
        _messages = messages;
        _variantTagSource = variantTagSource;
        _configProvider = configProvider;
        _indexService = indexService;
        _textSource = textSource;
        _parseCache = parseCache;
        _schema = schema;
        _handlerRegistry = handlerRegistry;
        _producers = producers.ToList();
        _logger = logger;
        _fileTypeRegistry = fileTypeRegistry;
        _fileHelper = fileHelper;
        _storyChainProblems = storyChainProblems;
        _storyGraphDiagnostics = storyGraphDiagnostics;
    }

    protected override string FileExtension => ".xml";

    /// <summary>Documents with a fix-token map; never pruned, so a workspace sweep fills it for every file.</summary>
    public CacheStatistics Snapshot()
    {
        return new CacheStatistics("xml-fixes", _fixCache.Count);
    }

    /// <summary>
    ///     The full pipeline for one document, publish-free (<see cref="IXmlDiagnosticsCollector" />) —
    ///     the push path below and on-demand consumers (story graph) share this body.
    /// </summary>
    public IReadOnlyList<Diagnostic> Collect(string uri, string text, GameIndex index)
    {
        var canonicalUri = _fileHelper.NormalizeUri(uri);
        // Built per run and bound to THIS index, so a cross-object rule sees the same workspace
        // state as every other check in the pass.
        var objects = new EffectiveObjectResolver(index, _schema, _variantTagSource);

        // The configured locale picks the schema's notes; the server's own texts come from the one
        // injected catalogue, which reads the same setting.
        var ctx = new DiagnosticsContext(_schema, index, canonicalUri, _configProvider.Current.Locale,
            _iconRepack.IconsAwaitingRepack, _modelTextures, _iconNames, objects)
        {
            Messages = _messages
        };

        // One parse shared by every producer - and via the parse cache, shared with the indexing
        // parse and every request handler touching the same content.
        var parsed = _parseCache.GetOrParse(canonicalUri, text);

        // Per-phase timing, reported only when a document turns out to be pathological. A handful
        // of files dominate a workspace sweep - one 497 KB prop file measured 23.4s against a
        // whole-workspace total of 83s, while a 4.2 MB Planets.xml was unremarkable - so the cost
        // does not track size and cannot be guessed from the outside. Two Stopwatch reads per
        // producer are nothing against the work they bracket, and without them the only visible
        // symptom is a sweep that appears to hang at a different point every run.
        var sw = Stopwatch.StartNew();
        var timings = new List<(string Phase, long Ms)>();

        void Phase(string name, Action work)
        {
            var at = sw.ElapsedMilliseconds;
            work();
            timings.Add((name, sw.ElapsedMilliseconds - at));
        }

        // Every producer in registration order, each over the same parse and the same index, and
        // each asked first whether the document is one it reads at all.
        var facts = new List<XmlFact>();
        foreach (var producer in _producers)
            if (producer.AppliesTo(canonicalUri))
                Phase(producer.Name, () => facts.AddRange(producer.Produce(canonicalUri, parsed, index)));

        var allDiags = new List<Diagnostic>();
        Phase("dispatch", () =>
        {
            foreach (var fact in facts)
            foreach (var result in _handlerRegistry.Dispatch(fact, ctx))
            {
                allDiags.Add(ToLspDiagnostic(fact, result));
            }
        });

        Phase("storyChain",
            () => allDiags.AddRange(
                _storyChainProblems.GetForDocument(canonicalUri)
                    .Select(p => ToLspDiagnostic(p, ctx.Messages))));

        if (IsStoryParserDocument(uri))
            Phase("storyGraph",
                () => allDiags.AddRange(
                    _storyGraphDiagnostics.GetForDocument(canonicalUri).Select(ToLspDiagnostic)));

        // Suppression is applied once, here, on the way out - after every producer and after the
        // story chain/graph sources, which build their Diagnostics without a handler. Filtering
        // earlier would mean teaching each producer about scopes, and would miss those two.
        //
        // It happens in Collect rather than at publish time because Collect is also the on-demand
        // entry point (IXmlDiagnosticsCollector), and the story editor calls it with staged text
        // that is not what a publish-time lookup would find.
        SuppressionScan scan = null!;
        Phase("suppressionScan", () => scan = SuppressionCommentParser.Parse(parsed.Html, IsObjectNode));

        // Complaints about the directives go through the same filter as everything else, so a user
        // who does not want them can suppress aetswg-013-* like any other group.
        allDiags.AddRange(scan.ProblemDiagnostics(parsed.Lines));

        IReadOnlyList<Diagnostic> filtered = [];
        Phase("filterSuppressed", () => filtered = FilterSuppressed(allDiags, scan.Ranges));

        // Reported AFTER every phase, not after dispatch: the first cut of this stopped at
        // dispatch and so could not see a document that was slow in the tail - which is exactly
        // the shape of the residual left once model textures were cached.
        if (sw.ElapsedMilliseconds >= SlowDocumentMs)
            _logger.LogWarning(
                "Slow diagnostics for {Uri}: {Total}ms over {Facts} fact(s) - {Breakdown}",
                uri, sw.ElapsedMilliseconds, facts.Count,
                string.Join(", ", timings.Where(t => t.Ms > 0).Select(t => $"{t.Phase} {t.Ms}ms")));

        return filtered;
    }

    public async Task RevalidateWorkspaceAsync(CancellationToken ct)
    {
        // Every early return below used to be silent, which is why "no diagnostics anywhere" was
        // indistinguishable from "a handler that did not fire" and took a day of inference from
        // timestamps. Whatever else is true, the log should be able to answer "did it publish?".
        ClearAllPublished();
        var index = _indexService.Current;
        _logger.LogInformation("Revalidating {Count} indexed document(s)", index.Documents.Count);

        // Timed by phase, because "the sweep stalls" is not actionable and the three candidates
        // look identical from outside: reading a closed document's text (disk), analysing it
        // (CPU), and handing the result to the transport (which can block on the client draining
        // it - this sweep pushes tens of MiB). The totals below say which, and the slowest single
        // document says whether it is spread evenly or is one pathological file.
        var swTotal = Stopwatch.StartNew();
        var swPhase = new Stopwatch();
        long textTicks = 0, publishTicks = 0;
        var done = 0;
        var slowestTicks = 0L;
        string? slowestUri = null;

        foreach (var uri in index.Documents.Keys)
        {
            ct.ThrowIfCancellationRequested();

            // The index holds every language's documents; reading a Lua script as XML put "No XML
            // declaration" on each of them.
            if (!Path.GetExtension(uri).Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            swPhase.Restart();
            var text = _textSource.GetText(_fileHelper.NormalizeUri(uri))?.Text;
            textTicks += swPhase.ElapsedTicks;

            if (text is null)
            {
                _logger.LogDebug("Revalidate {Uri} skipped: no text available", uri);
                continue;
            }

            swPhase.Restart();
            PublishForDocument(uri, text, index);
            publishTicks += swPhase.ElapsedTicks;

            if (swPhase.ElapsedTicks > slowestTicks)
            {
                slowestTicks = swPhase.ElapsedTicks;
                slowestUri = uri;
            }

            // Progress, so a long sweep is visibly progressing rather than apparently hung. A rig
            // watching the log cannot otherwise tell a stall from a finished run.
            if (++done % 250 == 0)
                _logger.LogInformation(
                    "Revalidate progress: {Done}/{Total} document(s) after {Elapsed}ms",
                    done, index.Documents.Count, swTotal.ElapsedMilliseconds);
        }

        _logger.LogInformation(
            "Revalidate complete: {Done} document(s) in {Elapsed}ms "
            + "(text {TextMs}ms, analyse+publish {PublishMs}ms); slowest '{SlowestUri}' at {SlowestMs}ms",
            done, swTotal.ElapsedMilliseconds,
            (long)TimeSpan.FromTicks(textTicks).TotalMilliseconds,
            (long)TimeSpan.FromTicks(publishTicks).TotalMilliseconds,
            slowestUri, (long)TimeSpan.FromTicks(slowestTicks).TotalMilliseconds);

        await Task.CompletedTask;
    }

    public Task RevalidateDocumentAsync(string uri, CancellationToken ct)
    {
        var index = _indexService.Current;
        var text = _textSource.GetText(_fileHelper.NormalizeUri(uri))?.Text;
        if (text is null)
        {
            // The text source holds what the client has opened plus what the indexer read. A miss
            // here is the difference between "diagnosed and clean" and "never looked at".
            _logger.LogDebug("Revalidate {Uri} skipped: no text available", uri);
            return Task.CompletedTask;
        }

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
    ///     Whether an element is an "object" for <c>aetswg:suppress-object</c> - the same test the
    ///     fact producer uses to decide what a tag belongs to: its name resolves to a schema object
    ///     type, in either the source or PascalCase spelling.
    /// </summary>
    private bool IsObjectNode(HtmlNode node)
    {
        return _schema.GetObjectType(node.Name) is not null
               || _schema.GetObjectType(XmlUtility.ToPascalCase(node.Name)) is not null;
    }

    /// <summary>
    ///     XML publishes diagnostics for every indexed document, not only the open ones, so its
    ///     republish has to cover the same set - refreshing open documents alone would leave stale
    ///     problems in the panel for files the user has not opened.
    /// </summary>
    public override Task RepublishAllAsync(CancellationToken ct)
    {
        return RevalidateWorkspaceAsync(ct);
    }

    protected override void PublishForDocument(string uri, string text, GameIndex index)
    {
        var sw = Stopwatch.StartNew();
        var allDiags = Collect(uri, text, index);
        var collectMs = sw.ElapsedMilliseconds;

        var normalizedUri = _fileHelper.NormalizeUri(uri);
        var fixes = new Dictionary<(int, int), string>();
        foreach (var d in allDiags)
        {
            var fixToken = d.Data?["fix"]?.Value<string>();
            if (fixToken is not null)
                fixes[(d.Range.Start.Line, d.Range.Start.Character)] = fixToken;
        }

        _fixCache[normalizedUri] = fixes;

        _logger.LogDebug("Publishing {Count} diagnostic(s) for {Uri}", allDiags.Count, uri);

        var atPublish = sw.ElapsedMilliseconds;
        Publish(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = new Container<Diagnostic>(allDiags)
        });
        var publishMs = sw.ElapsedMilliseconds - atPublish;

        // The handoff to the transport, timed apart from the analysis. A sweep pushes tens of MiB
        // at the client, and if it stops draining, the cost lands HERE - on whichever document
        // happens to be in flight rather than on one that did anything expensive. That is the
        // shape of the residual: seconds attributed to a 161-byte file, a different file each run.
        if (publishMs >= SlowDocumentMs)
            _logger.LogWarning(
                "Slow publish for {Uri}: {Publish}ms handing {Count} diagnostic(s) to the transport "
                + "(analysis was {Collect}ms)",
                uri, publishMs, allDiags.Count, collectMs);
    }

    private bool IsStoryParserDocument(string documentUri)
    {
        return _fileTypeRegistry.GetTypesForFile(_fileHelper.NormalizeUri(documentUri)).Contains("StoryParser");
    }

    private static Diagnostic ToLspDiagnostic(StoryChainProblem problem, IMessageCatalog messages)
    {
        return new Diagnostic
        {
            Severity = problem.Severity.ToLsp(),
            Message = messages.Format(problem.Message),
            Range = SafeRange(problem.Line, problem.Column, problem.EndLine, problem.EndColumn),
            Source = AppProperties.LspServerId,
            Code = DiagnosticIds.StoryChain.ToString()
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
            Code = DiagnosticIds.StoryGraph.ToString()
        };
    }

    private static Diagnostic ToLspDiagnostic(XmlFact fact, XmlDiagnosticResult result)
    {
        var line = result.OverrideLine ?? fact.Line;
        var col = result.OverrideColumn ?? fact.Column;
        var endLine = result.OverrideEndLine ?? fact.EndLine;

        // A cross-line span (fact or result carries an explicit end line) wins over the default
        // same-line col+length range - used e.g. to grey out a whole multi-line element.
        var range = endLine is { } el
            ? SafeRange(line, col, el, result.OverrideEndColumn ?? fact.EndColumn ?? 0)
            : SafeRange(line, col, result.OverrideLength ?? fact.Length);

        return new Diagnostic
        {
            Severity = result.Severity.ToLsp(),
            Message = result.Message,
            Range = range,
            Source = AppProperties.LspServerId,
            Code = result.Id is { } id ? (DiagnosticCode?)id.ToString() : null,
            Tags = MapTags(result.Tags),
            Data = BuildDiagnosticData(result),
            RelatedInformation = MapRelatedInformation(result.RelatedLocations)
        };
    }

    /// <summary>Test seam over the private mapping, so the id-to-code wiring can be pinned directly.</summary>
    internal static Diagnostic ToLspDiagnosticForTest(XmlFact fact, XmlDiagnosticResult result)
    {
        return ToLspDiagnostic(fact, result);
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
    // multi-line element) - clamp endLine to at least startLine so a malformed/negative value can't
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
            !result.OfferRemoveEarlierDuplicates && result.EngineRepair is null)
            return null;

        var obj = new JObject();
        if (result.SuggestedFix is not null)
            obj["fix"] = result.SuggestedFix;
        if (result.FixTitle is not null)
            obj["fixTitle"] = result.FixTitle;
        if (result.EngineRepair is not null)
            obj["engineRepair"] = new JObject
            {
                ["title"] = result.EngineRepair.Title,
                ["edits"] = new JArray(result.EngineRepair.Edits.Select(e => new JObject
                {
                    ["line"] = e.Line,
                    ["column"] = e.Column,
                    ["length"] = e.Length,
                    ["newText"] = e.NewText
                }))
            };
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