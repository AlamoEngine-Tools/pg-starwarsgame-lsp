// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Loretta.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticCode = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticCode;
using LspDiagnosticContainer = OmniSharp.Extensions.LanguageServer.Protocol.Models.Container<
    OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspPublishParams = OmniSharp.Extensions.LanguageServer.Protocol.Models.PublishDiagnosticsParams;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Diagnostics;

public sealed class LuaDiagnosticsPublisher : DiagnosticsPublisherBase
{
    private readonly ILspConfigurationProvider? _configProvider;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<LuaDiagnosticsPublisher> _logger;
    private readonly ILuaParseCache _parseCache;
    private readonly ILuaApiSchemaProvider _schemaProvider;
    private readonly ILuaAnalyzer? _analyzer;
    private readonly IGameWorkspaceHost _workspaceHost;

    // The analyzer's latest findings per document, already mapped to this server's ids.
    private readonly ConcurrentDictionary<string, IReadOnlyList<LspDiagnostic>> _analyzerFindings =
        new(DocumentUris.Comparer);

    public LuaDiagnosticsPublisher(
        ILanguageServerFacade server,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaDiagnosticsPublisher> logger,
        ILuaParseCache parseCache,
        ILspConfigurationProvider configProvider,
        // REQUIRED: the layer map names the project in every diagnostic's source, and an optional
        // dependency is the one shape the container is free to skip without a word.
        IProjectLayerMap layerMap,
        ServerOptions? options = null,
        IGlobalSuppressionStore? globalSuppressions = null,
        ILuaAnalyzer? analyzer = null)
        : this(p => server.TextDocument.PublishDiagnostics(p),
            indexService, workspaceHost, fileHelper, schemaProvider, logger,
            (int)(options ?? ServerOptions.Default).DiagnosticsDebounce.TotalMilliseconds,
            parseCache, configProvider, globalSuppressions, layerMap, analyzer)
    {
    }

    internal LuaDiagnosticsPublisher(
        Action<LspPublishParams> publish,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaDiagnosticsPublisher> logger,
        int debounceMs = 0,
        ILuaParseCache? parseCache = null,
        ILspConfigurationProvider? configProvider = null,
        IGlobalSuppressionStore? globalSuppressions = null,
        IProjectLayerMap? layerMap = null,
        ILuaAnalyzer? analyzer = null)
        : base(publish, indexService, workspaceHost, debounceMs, logger, globalSuppressions, layerMap)
    {
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _logger = logger;
        _configProvider = configProvider;
        _parseCache = parseCache ?? new LuaParseCache(
            new DocumentTextSource(workspaceHost, fileHelper, NullLogger<DocumentTextSource>.Instance),
            ServerOptions.Default.ParseCacheCapacity);
        _analyzer = analyzer;
        _workspaceHost = workspaceHost;
        if (analyzer is not null) analyzer.DiagnosticsPublished += OnAnalyzerPublished;
    }

    // The analyzer reports on its own schedule, per document: keep the kept findings and publish
    // that document again, so the editor sees one set from one server.
    private void OnAnalyzerPublished(LspPublishParams p)
    {
        // The analyzer spells URIs its own way; the open document keeps the editor's spelling, and
        // that is the key it is published under.
        var analyzerUri = _fileHelper.NormalizeUri(p.Uri.ToString());
        var uri = _workspaceHost.All.FirstOrDefault(d => DocumentUris.Same(d.Uri, analyzerUri))?.Uri ?? analyzerUri;
        _analyzerFindings[uri] = p.Diagnostics
            .Select(LuaAnalyzerRulePolicy.Map)
            .OfType<LspDiagnostic>()
            .ToList();
        RepublishDocument(uri);
    }

    protected override string FileExtension => ".lua";

    // Feature-flag gate: a null provider (test convenience ctor) means always enabled.
    protected override bool DiagnosticsEnabled =>
        _configProvider?.Current.Features.Lua.Diagnostics ?? true;

    protected override void PublishForDocument(string uri, string text, GameIndex index)
    {
        var diagnostics = new List<LspDiagnostic>();

        // One parse shared by the syntax-error pass and all three analyzers (previously four
        // separate parses of the same text) - and via the cache, with indexing and every request
        // handler touching the same content.
        var parsed = _parseCache.GetOrParse(_fileHelper.NormalizeUri(uri), text);

        // One voice on syntax: the analyzer's while it runs, this server's own parser otherwise.
        if (_analyzer is { IsRunning: true })
        {
            if (_analyzerFindings.TryGetValue(_fileHelper.NormalizeUri(uri), out var found))
                diagnostics.AddRange(found);
        }
        else
        {
            CollectSyntaxErrors(parsed.Tree, diagnostics);
        }

        CollectReferenceErrors(uri, index, diagnostics);
        diagnostics.AddRange(LuaImportAnalyzer.Analyze(uri, parsed.Tree, index.Documents, _fileHelper));
        diagnostics.AddRange(LuaGlobalScopeAnalyzer.Analyze(uri, parsed.Tree, index, _schemaProvider, _fileHelper));
        diagnostics.AddRange(LuaUpvalueAnalyzer.Analyze(parsed.Tree, uri));
        diagnostics.AddRange(LuaPlanAnalyzer.Analyze(uri, parsed.Tree));

        // Applied once, on the way out, so every analyzer above is covered without any of them
        // having to know about scopes - and using the parse already in hand.
        var scan = LuaSuppressionCommentParser.Parse(parsed.Tree);
        diagnostics.AddRange(scan.ProblemDiagnostics(text.Split('\n')));

        var kept = FilterSuppressed(diagnostics, scan.Ranges);

        Publish(new LspPublishParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = new LspDiagnosticContainer(kept)
        });
    }

    private static void CollectSyntaxErrors(SyntaxTree tree, List<LspDiagnostic> diagnostics)
    {
        foreach (var diag in tree.GetDiagnostics())
        {
            if (diag.Severity == DiagnosticSeverity.Hidden) continue;
            var lspSeverity = diag.Severity switch
            {
                DiagnosticSeverity.Error => LspDiagnosticSeverity.Error,
                DiagnosticSeverity.Warning => LspDiagnosticSeverity.Warning,
                _ => LspDiagnosticSeverity.Information
            };
            var span = diag.Location.GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;

            // Loretta's own id where it maps, its raw id where it does not: an unmapped code is
            // still worth showing, it just cannot be named by a suppression.
            var code = LorettaDiagnosticIds.TryMap(diag.Id, out var mapped) ? mapped.ToString() : diag.Id;

            diagnostics.Add(new LspDiagnostic
            {
                Code = new LspDiagnosticCode(code),
                Severity = lspSeverity,
                Message = diag.GetMessage(),
                Range = new LspRange(
                    new LspPosition(start.Line, start.Character),
                    new LspPosition(end.Line, end.Character)),
                Source = AppProperties.LspServerId
            });
        }
    }

    private void CollectReferenceErrors(string uri, GameIndex index, List<LspDiagnostic> diagnostics)
    {
        var canonicalUri = _fileHelper.NormalizeUri(uri);
        if (!index.Documents.TryGetValue(canonicalUri, out var docIndex))
            return;

        foreach (var reference in docIndex.References)
        {
            var range = new LspRange(
                new LspPosition(reference.Line, reference.Column),
                new LspPosition(reference.Line, reference.Column + reference.Length));

            switch (reference.ExpectedKind)
            {
                case GameSymbolKind.XmlObject:
                {
                    var resolved = index.Resolve(reference.TargetId);
                    var eval = ReferenceResolutionEvaluator.Evaluate(reference.TargetId, reference.ExpectedTypeName,
                        resolved);
                    if (eval is null) continue;

                    diagnostics.Add(new LspDiagnostic
                    {
                        // Same id the XML side uses: an unresolved reference is the same kind of
                        // problem whichever language names the target. Taken from the evaluator
                        // rather than named here, so the two cannot disagree once this side is
                        // taught about indexed types - it passes none today, so every id it sees
                        // is still UnresolvedReference.
                        Code = new LspDiagnosticCode(eval.Value.Id.ToString()),
                        Severity = eval.Value.Severity.ToLsp(),
                        Message = eval.Value.Message,
                        Range = range,
                        Source = AppProperties.LspServerId
                    });
                    break;
                }
                case GameSymbolKind.LocalisationKey:
                    if (index.Localisation.ContainsKey(reference.TargetId)) continue;

                    // Same id and wording as the XML side's text-key check.
                    diagnostics.Add(new LspDiagnostic
                    {
                        Code = new LspDiagnosticCode(DiagnosticIds.LocalisationKeyExistence.ToString()),
                        Severity = LspDiagnosticSeverity.Warning,
                        Message =
                            $"Localisation key '{reference.TargetId}' was not found in the loaded translation databases.",
                        Range = range,
                        Source = AppProperties.LspServerId
                    });
                    break;
                default:
                    // LuaGlobal call sites are rename data, not something to resolve; assets and
                    // workspace files have no Lua-side check yet.
                    continue;
            }
        }
    }
}