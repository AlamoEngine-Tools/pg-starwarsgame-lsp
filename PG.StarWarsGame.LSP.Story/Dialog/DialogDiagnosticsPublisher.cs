// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>Direct revalidation entry points for the dialog document sync handler.</summary>
public interface IDialogDiagnosticsRevalidator
{
    Task RevalidateDocumentAsync(string uri, CancellationToken ct);

    /// <summary>Publishes empty diagnostics for a closed document.</summary>
    void ClearDocument(string uri);
}

/// <summary>
///     Diagnostics for story-dialog .txt scripts. Only documents inside the
///     <see cref="IStoryDialogScope" /> registry scope produce diagnostics - anything else
///     publishes empty. Index changes (localisation, symbols) re-run open dialog documents via
///     the shared base; the sync handler drives open/change revalidation directly because dialog
///     files never enter the GameIndex.
/// </summary>
public sealed class DialogDiagnosticsPublisher : DiagnosticsPublisherBase, IDialogDiagnosticsRevalidator
{
    private readonly DialogFactProducer _factProducer;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly DialogDiagnosticsHandlerRegistry _registry;
    private readonly IStoryDialogScope _scope;
    private readonly IDocumentTextSource _textSource;

    public DialogDiagnosticsPublisher(
        ILanguageServerFacade server,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IStoryDialogScope scope,
        DialogFactProducer factProducer,
        DialogDiagnosticsHandlerRegistry registry,
        IFileHelper fileHelper,
        IDocumentTextSource textSource,
        ILogger<DialogDiagnosticsPublisher> logger,
        ServerOptions? options = null)
        : this(p => server.TextDocument.PublishDiagnostics(p), indexService, workspaceHost, scope,
            factProducer, registry, fileHelper, textSource,
            (int)(options ?? ServerOptions.Default).DiagnosticsDebounce.TotalMilliseconds, logger)
    {
    }

    internal DialogDiagnosticsPublisher(
        Action<PublishDiagnosticsParams> publish,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IStoryDialogScope scope,
        DialogFactProducer factProducer,
        DialogDiagnosticsHandlerRegistry registry,
        IFileHelper fileHelper,
        IDocumentTextSource? textSource = null,
        int debounceMs = 0,
        ILogger<DialogDiagnosticsPublisher>? logger = null)
        : base(publish, indexService, workspaceHost, debounceMs, logger)
    {
        _indexService = indexService;
        _scope = scope;
        _factProducer = factProducer;
        _registry = registry;
        _fileHelper = fileHelper;
        _textSource = textSource ?? new DocumentTextSource(workspaceHost, fileHelper,
            NullLogger<DocumentTextSource>.Instance);
    }

    protected override string FileExtension => ".txt";

    protected override bool DiagnosticsEnabled => _scope.Enabled;

    public Task RevalidateDocumentAsync(string uri, CancellationToken ct)
    {
        if (!DiagnosticsEnabled) return Task.CompletedTask;

        var canonicalUri = _fileHelper.NormalizeUri(uri);
        var text = _textSource.GetText(canonicalUri)?.Text;
        if (text is null) return Task.CompletedTask;

        PublishForDocument(uri, text, _indexService.Current);
        return Task.CompletedTask;
    }

    public void ClearDocument(string uri)
    {
        Publish(EmptyParams(_fileHelper.NormalizeUri(uri)));
    }

    protected override void PublishForDocument(string uri, string text, GameIndex index)
    {
        var canonicalUri = _fileHelper.NormalizeUri(uri);
        if (!_scope.IsInScope(canonicalUri))
        {
            // A .txt outside the registry scope is not a dialog script; publishing empty also
            // clears anything stale from a scope change.
            Publish(EmptyParams(canonicalUri));
            return;
        }

        var document = StoryDialogParser.Parse(text);
        var diagnostics = new List<Diagnostic>();

        foreach (var problem in document.Problems)
            diagnostics.Add(ToLspDiagnostic(new DialogDiagnostic(XmlDiagnosticSeverity.Error,
                problem.Message, problem.Line, problem.Column, problem.EndColumn)));

        foreach (var fact in _factProducer.Produce(document, canonicalUri))
        foreach (var diagnostic in _registry.Dispatch(fact, index))
            diagnostics.Add(ToLspDiagnostic(diagnostic));

        Publish(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(canonicalUri),
            Diagnostics = new Container<Diagnostic>(diagnostics)
        });
    }

    private static Diagnostic ToLspDiagnostic(DialogDiagnostic diagnostic)
    {
        return new Diagnostic
        {
            Severity = diagnostic.Severity.ToLsp(),
            Message = diagnostic.Message,
            Range = new Range(diagnostic.Line, diagnostic.Column, diagnostic.Line, diagnostic.EndColumn),
            Source = AppProperties.LspServerId,
            Code = "story-dialog"
        };
    }
}