// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Schema;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticContainer = OmniSharp.Extensions.LanguageServer.Protocol.Models.Container<
    OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspPublishParams = OmniSharp.Extensions.LanguageServer.Protocol.Models.PublishDiagnosticsParams;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Diagnostics;

public sealed class LuaDiagnosticsPublisher : DiagnosticsPublisherBase
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly IFileHelper _fileHelper;
    private readonly ILogger<LuaDiagnosticsPublisher> _logger;
    private readonly ILuaApiSchemaProvider _schemaProvider;

    public LuaDiagnosticsPublisher(
        ILanguageServerFacade server,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaDiagnosticsPublisher> logger)
        : this(p => server.TextDocument.PublishDiagnostics(p),
            indexService, workspaceHost, fileHelper, schemaProvider, logger, debounceMs: 100)
    {
    }

    internal LuaDiagnosticsPublisher(
        Action<LspPublishParams> publish,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaDiagnosticsPublisher> logger,
        int debounceMs = 0)
        : base(publish, indexService, workspaceHost, debounceMs)
    {
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _logger = logger;
    }

    protected override string FileExtension => ".lua";

    protected override void PublishForDocument(string uri, string text, GameIndex index)
    {
        var diagnostics = new List<LspDiagnostic>();

        CollectSyntaxErrors(text, diagnostics);
        CollectReferenceErrors(uri, index, diagnostics);
        diagnostics.AddRange(LuaImportAnalyzer.Analyze(uri, text, index.Documents.Keys, _fileHelper));
        diagnostics.AddRange(LuaGlobalScopeAnalyzer.Analyze(uri, text, index, _schemaProvider, _fileHelper));

        Publish(new LspPublishParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = new LspDiagnosticContainer(diagnostics)
        });
    }

    private static void CollectSyntaxErrors(string text, List<LspDiagnostic> diagnostics)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
        foreach (var diag in tree.GetDiagnostics())
        {
            if (diag.Severity != DiagnosticSeverity.Error) continue;
            var span = diag.Location.GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;
            diagnostics.Add(new LspDiagnostic
            {
                Severity = LspDiagnosticSeverity.Error,
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
            var range = new LspRange(
                new LspPosition(reference.Line, reference.Column),
                new LspPosition(reference.Line, reference.Column + reference.Length));

            if (resolved is null)
                diagnostics.Add(new LspDiagnostic
                {
                    Severity = LspDiagnosticSeverity.Error,
                    Message = $"'{reference.TargetId}' could not be resolved to any known game object.",
                    Range = range,
                    Source = AppProperties.LspServerId
                });
            else if (reference.ExpectedTypeName is not null &&
                     !string.Equals(resolved.TypeName, reference.ExpectedTypeName,
                         StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(new LspDiagnostic
                {
                    Severity = LspDiagnosticSeverity.Warning,
                    Message =
                        $"'{reference.TargetId}' is a '{resolved.TypeName}', expected '{reference.ExpectedTypeName}'.",
                    Range = range,
                    Source = AppProperties.LspServerId
                });
        }
    }
}