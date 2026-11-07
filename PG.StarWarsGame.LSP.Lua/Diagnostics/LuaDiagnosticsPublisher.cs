// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Loretta.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Messages;
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
using LspHover = OmniSharp.Extensions.LanguageServer.Protocol.Models.Hover;
using LspHoverParams = OmniSharp.Extensions.LanguageServer.Protocol.Models.HoverParams;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspPublishParams = OmniSharp.Extensions.LanguageServer.Protocol.Models.PublishDiagnosticsParams;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using LspTextDocumentIdentifier = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentIdentifier;

namespace PG.StarWarsGame.LSP.Lua.Diagnostics;

public sealed class LuaDiagnosticsPublisher : DiagnosticsPublisherBase
{
    private readonly ILuaAnalyzer _analyzer;

    // The analyzer's latest findings per document, already mapped to this server's ids.
    private readonly ConcurrentDictionary<string, IReadOnlyList<LspDiagnostic>> _analyzerFindings =
        new(DocumentUris.Comparer);

    // How many reports each document has had: a report whose receivers are still being hovered is
    // stored only if no later one arrived meanwhile.
    private readonly ConcurrentDictionary<string, int> _analyzerReports = new(DocumentUris.Comparer);

    private readonly IFileHelper _fileHelper;
    private readonly ILogger<LuaDiagnosticsPublisher> _logger;
    private readonly ILuaParseCache _parseCache;
    private readonly ILuaApiSchemaProvider _schemaProvider;
    private readonly IGameWorkspaceHost _workspaceHost;
    private readonly IMessageCatalog _messages;

    // Every dependency is REQUIRED and there is one constructor: an optional parameter is the one
    // shape the container is free to skip without a word, and a second constructor is the one it
    // falls back to. The layer map names the project in every diagnostic's source; the analyzer's
    // findings are folded into ours (#153); the parse cache is the shared one, not a private
    // second copy that splits the statistics (#305).
    public LuaDiagnosticsPublisher(
        IDiagnosticsSink sink,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaDiagnosticsPublisher> logger,
        ILuaParseCache parseCache,
        IMessageCatalog messages,
        IProjectLayerMap layerMap,
        ServerOptions options,
        IGlobalSuppressionStore globalSuppressions,
        ILuaAnalyzer analyzer)
        : base(sink, indexService, workspaceHost, options, logger, globalSuppressions, layerMap)
    {
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _logger = logger;
        _messages = messages;
        _parseCache = parseCache;
        _analyzer = analyzer;
        _workspaceHost = workspaceHost;
        analyzer.DiagnosticsPublished += OnAnalyzerPublished;
    }

    protected override string FileExtension => ".lua";

    // The analyzer reports on its own schedule, per document: keep the kept findings and publish
    // that document again, so the editor sees one set from one server.
    private void OnAnalyzerPublished(LspPublishParams p)
    {
        // The analyzer spells URIs its own way; the open document keeps the editor's spelling, and
        // that is the key it is published under.
        var analyzerUri = _fileHelper.NormalizeUri(p.Uri.ToString());
        var uri = _workspaceHost.All.FirstOrDefault(d => DocumentUris.Same(d.Uri, analyzerUri))?.Uri ?? analyzerUri;

        // Every finding, kept or not, so the rules the policy drops can be counted (#278): in the
        // editor a dropped finding leaves no trace. The report line comes first, so a census keeps
        // each document's last report - an early one carries findings a later one withdraws.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Analyzer report {Uri} - {Count} findings", uri, p.Diagnostics.Count());
            foreach (var finding in p.Diagnostics)
                _logger.LogDebug("Analyzer finding {Rule} {Fate} at {Uri}:{Line} - {Message}",
                    finding.Code?.String ?? finding.Code?.Long.ToString() ?? "",
                    LuaAnalyzerRulePolicy.Map(finding) is null ? "dropped" : "kept",
                    uri, finding.Range.Start.Line + 1, finding.Message);
        }

        var mapped = p.Diagnostics
            .Select(LuaAnalyzerRulePolicy.Map)
            .OfType<LspDiagnostic>()
            .ToList();
        var report = _analyzerReports.AddOrUpdate(uri, 1, (_, n) => n + 1);
        _ = StoreAsync(uri, report, mapped);
    }

    // An undefined member holds only on an engine type, which defines all of its members; the
    // analyzer's hover names the receiver's type. Awaited, never waited on: the event is raised on
    // the analyzer's reader thread, which also reads the hovers' answers.
    private async Task StoreAsync(string uri, int report, List<LspDiagnostic> mapped)
    {
        try
        {
            var kept = new List<LspDiagnostic>(mapped.Count);
            foreach (var finding in mapped)
                if (!LuaAnalyzerRulePolicy.IsUndefinedField(finding) || await IsOnEngineTypeAsync(uri, finding))
                    kept.Add(finding);
            if (_analyzerReports.GetValueOrDefault(uri) != report) return;
            _analyzerFindings[uri] = kept;
            RepublishDocument(uri);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Analyzer report {Uri} not stored", uri);
        }
    }

    private async Task<bool> IsOnEngineTypeAsync(string uri, LspDiagnostic finding)
    {
        var document = _workspaceHost.All.FirstOrDefault(d => DocumentUris.Same(d.Uri, uri));
        if (document is null) return false;
        var tree = _parseCache.GetOrParse(_fileHelper.NormalizeUri(uri), document.Text).Tree;
        if (LuaAnalyzerRulePolicy.ReceiverPosition(finding, tree) is not { } at) return false;
        var hover = await _analyzer.RequestAsync<LspHover>("textDocument/hover", new LspHoverParams
        {
            TextDocument = new LspTextDocumentIdentifier(DocumentUri.From(uri)),
            Position = at
        }, CancellationToken.None);
        return LuaAnalyzerRulePolicy.ReceiverTypeOf(hover) is { } type &&
               _schemaProvider.EngineClassNames.Contains(type);
    }

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
                diagnostics.AddRange(found.Where(f =>
                    LuaAnalyzerRulePolicy.Holds(f, parsed.Tree, _schemaProvider.DeclaredGlobalNames)));
        }
        else
        {
            CollectSyntaxErrors(parsed.Tree, diagnostics);
        }

        // What 5.1 added: both parsers read 5.1 and accept it, the game's 5.0 does not (#157).
        diagnostics.AddRange(LuaVersionSyntaxAnalyzer.Analyze(parsed.Tree, _messages));

        CollectReferenceErrors(uri, index, diagnostics, _messages);
        diagnostics.AddRange(LuaImportAnalyzer.Analyze(uri, parsed.Tree, index.Documents, _fileHelper, _messages));
        diagnostics.AddRange(LuaGlobalScopeAnalyzer.Analyze(uri, parsed.Tree, index, _schemaProvider, _fileHelper,
            _messages));
        diagnostics.AddRange(LuaUpvalueAnalyzer.Analyze(parsed.Tree, uri, _messages));
        diagnostics.AddRange(LuaPlanAnalyzer.Analyze(uri, parsed.Tree, _messages));
        diagnostics.AddRange(LuaCallFormAnalyzer.Analyze(uri, parsed.Tree, _schemaProvider, _messages));
        diagnostics.AddRange(LuaStateMachineAnalyzer.Analyze(uri, parsed.Tree,
            LuaStateMachineContract.For(_fileHelper.NormalizeUri(uri), index.Documents, _parseCache, _fileHelper),
            _messages));
        diagnostics.AddRange(LuaPooledStateAnalyzer.Analyze(uri, parsed.Tree,
            LuaPooledState.For(_fileHelper.NormalizeUri(uri), parsed.Tree.GetRoot(), index.Documents, _parseCache,
                _fileHelper), _messages));

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

    private void CollectReferenceErrors(string uri, GameIndex index, List<LspDiagnostic> diagnostics,
        IMessageCatalog messages)
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
                    var resolved = index.ResolveReference(reference);
                    // The indexed types go along, as on the XML side: a type nothing indexes - no
                    // workspace file of it, none in the baseline - is a check the tool cannot make,
                    // and saying "no object with this name exists" there is false about the data.
                    var eval = ReferenceResolutionEvaluator.Evaluate(reference.TargetId, reference.ExpectedTypeName,
                        resolved, messages, index.IndexedTypeNames, exactCase: reference.ExactCase);
                    if (eval is null) continue;

                    diagnostics.Add(new LspDiagnostic
                    {
                        // Same id the XML side uses: an unresolved reference is the same kind of
                        // problem whichever language names the target. Taken from the evaluator
                        // rather than named here, so the two cannot disagree about which id an
                        // outcome carries.
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
                            messages.Format("LocalisationKeyHandlerBase_LocalisationKeyWasNotFound",
                                reference.TargetId),
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