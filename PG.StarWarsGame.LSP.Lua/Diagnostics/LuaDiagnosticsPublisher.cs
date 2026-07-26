// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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

    public LuaDiagnosticsPublisher(
        ILanguageServerFacade server,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaDiagnosticsPublisher> logger,
        ILuaParseCache parseCache,
        ILspConfigurationProvider configProvider,
        ServerOptions? options = null,
        IGlobalSuppressionStore? globalSuppressions = null)
        : this(p => server.TextDocument.PublishDiagnostics(p),
            indexService, workspaceHost, fileHelper, schemaProvider, logger,
            (int)(options ?? ServerOptions.Default).DiagnosticsDebounce.TotalMilliseconds,
            parseCache, configProvider, globalSuppressions)
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
        IGlobalSuppressionStore? globalSuppressions = null)
        : base(publish, indexService, workspaceHost, debounceMs, logger, globalSuppressions)
    {
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _logger = logger;
        _configProvider = configProvider;
        _parseCache = parseCache ?? new LuaParseCache(
            new DocumentTextSource(workspaceHost, fileHelper, NullLogger<DocumentTextSource>.Instance),
            ServerOptions.Default.ParseCacheCapacity);
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

        CollectSyntaxErrors(parsed.Tree, diagnostics);
        CollectReferenceErrors(uri, index, diagnostics);
        diagnostics.AddRange(LuaImportAnalyzer.Analyze(uri, parsed.Tree, index.Documents, _fileHelper));
        diagnostics.AddRange(LuaGlobalScopeAnalyzer.Analyze(uri, parsed.Tree, index, _schemaProvider, _fileHelper));
        diagnostics.AddRange(LuaUpvalueAnalyzer.Analyze(parsed.Tree, uri));

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
            if (reference.ExpectedKind != GameSymbolKind.XmlObject) continue;

            var resolved = index.Resolve(reference.TargetId);
            var eval = ReferenceResolutionEvaluator.Evaluate(reference.TargetId, reference.ExpectedTypeName, resolved);
            if (eval is null) continue;

            var range = new LspRange(
                new LspPosition(reference.Line, reference.Column),
                new LspPosition(reference.Line, reference.Column + reference.Length));

            diagnostics.Add(new LspDiagnostic
            {
                // Same id the XML side uses: an unresolved reference is the same kind of problem
                // whichever language names the target.
                Code = new LspDiagnosticCode(DiagnosticIds.UnresolvedReference.ToString()),
                Severity = eval.Value.Severity.ToLsp(),
                Message = eval.Value.Message,
                Range = range,
                Source = AppProperties.LspServerId
            });
        }
    }
}