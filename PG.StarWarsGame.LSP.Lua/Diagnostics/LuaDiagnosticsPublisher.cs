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
        ServerOptions? options = null)
        : this(p => server.TextDocument.PublishDiagnostics(p),
            indexService, workspaceHost, fileHelper, schemaProvider, logger,
            (int)(options ?? ServerOptions.Default).DiagnosticsDebounce.TotalMilliseconds,
            parseCache, configProvider)
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
        ILspConfigurationProvider? configProvider = null)
        : base(publish, indexService, workspaceHost, debounceMs, logger)
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

        Publish(new LspPublishParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = new LspDiagnosticContainer(diagnostics)
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
            diagnostics.Add(new LspDiagnostic
            {
                Code = new LspDiagnosticCode(diag.Id),
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
                Severity = eval.Value.Severity.ToLsp(),
                Message = eval.Value.Message,
                Range = range,
                Source = AppProperties.LspServerId
            });
        }
    }
}