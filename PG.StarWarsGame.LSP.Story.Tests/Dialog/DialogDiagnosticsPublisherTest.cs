// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Story.Dialog;
using PG.StarWarsGame.LSP.Story.Dialog.Handlers;

namespace PG.StarWarsGame.LSP.Story.Tests.Dialog;

public sealed class DialogDiagnosticsPublisherTest
{
    private const string InScopeUri = "file:///ws/data/scripts/story/dialog_test.txt";
    private const string OutOfScopeUri = "file:///ws/readme.txt";

    private static (DialogDiagnosticsPublisher Publisher,
        List<PublishDiagnosticsParams> Published,
        FakeGameWorkspaceHost Host,
        FakeGameIndexService IndexService) Build(FakeDialogScope? scope = null)
    {
        var sink = new RecordingDiagnosticsSink();
        var host = new FakeGameWorkspaceHost();
        var indexService = new FakeGameIndexService();
        var fileHelper = new FileHelper(new MockFileSystem());
        var publisher = new DialogDiagnosticsPublisher(
            sink,
            indexService,
            host,
            scope ?? new FakeDialogScope(),
            new DialogFactProducer(new SchemaProviderBuilder().Build()),
            new DialogDiagnosticsHandlerRegistry([new UnknownDialogCommandHandler()]),
            fileHelper,
            new DocumentTextSource(host, fileHelper, NullLogger<DocumentTextSource>.Instance),
            NullLogger<DialogDiagnosticsPublisher>.Instance,
            new ProjectLayerMap(fileHelper),
            TestServerOptions.Immediate,
            new FakeGlobalSuppressionStore(),
            ResxMessageCatalog.Server("en"));
        return (publisher, sink.Published, host, indexService);
    }

    [Fact]
    public async Task RevalidateDocument_InScopeFile_PublishesDialogDiagnostics()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(InScopeUri, "[CHAPTER 0]\nBOGUS_COMMAND arg", 1);

        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        var diag = Assert.Single(Assert.Single(published).Diagnostics);
        Assert.Contains("BOGUS_COMMAND", diag.Message);
        // A real id, not a single catch-all code: without one it could not be suppressed, nor told
        // apart from any other dialog problem.
        Assert.Equal(DiagnosticIds.DialogUnknownCommand.ToString(), diag.Code!.Value.String);
    }

    [Fact]
    public async Task RevalidateDocument_SuppressionDirective_SilencesTheDiagnostic()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(InScopeUri,
            $"[CHAPTER 0]\n# aetswg:suppress {DiagnosticIds.DialogUnknownCommand} reason:: engine accepts it\n" +
            "BOGUS_COMMAND arg", 1);

        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        Assert.Empty(Assert.Single(published).Diagnostics);
    }

    [Fact]
    public async Task RevalidateDocument_SuppressionOfAnotherId_LeavesTheDiagnosticAlone()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(InScopeUri,
            $"[CHAPTER 0]\n# aetswg:suppress {DiagnosticIds.DialogCommandArity}\nBOGUS_COMMAND arg", 1);

        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        Assert.Single(Assert.Single(published).Diagnostics);
    }

    [Fact]
    public async Task RevalidateDocument_ParseProblem_IsPublished()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(InScopeUri, "[CHAPTER X]", 1);

        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        var diag = Assert.Single(Assert.Single(published).Diagnostics);
        Assert.Contains("chapter header", diag.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public async Task RevalidateDocument_OutOfScopeTxt_PublishesEmpty()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(OutOfScopeUri, "just some notes, not a dialog script", 1);

        await publisher.RevalidateDocumentAsync(OutOfScopeUri, CancellationToken.None);

        Assert.Empty(Assert.Single(published).Diagnostics);
    }

    [Fact]
    public async Task RevalidateDocument_ScopeDisabled_PublishesNothing()
    {
        var (publisher, published, host, _) = Build(new FakeDialogScope { Enabled = false });
        host.AddOrUpdate(InScopeUri, "[CHAPTER 0]\nBOGUS", 1);

        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        Assert.Empty(published);
    }

    [Fact]
    public async Task ClearDocument_PublishesEmptyDiagnostics()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(InScopeUri, "[CHAPTER 0]\nBOGUS", 1);
        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        publisher.ClearDocument(InScopeUri);

        Assert.Equal(2, published.Count);
        Assert.Empty(published[1].Diagnostics);
    }

    [Fact]
    public void IndexChanged_RerunsOpenDialogDocuments()
    {
        // Localisation or symbol updates arrive as index changes; open dialog docs re-validate.
        var (_, published, host, indexService) = Build();
        host.AddOrUpdate(InScopeUri, "[CHAPTER 0]\nBOGUS", 1);

        indexService.Raise(GameIndex.Empty);

        var diag = Assert.Single(Assert.Single(published).Diagnostics);
        Assert.Contains("BOGUS", diag.Message);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    internal sealed class FakeDialogScope : IStoryDialogScope
    {
        public bool Enabled { get; init; } = true;

        public bool IsInScope(string canonicalUri)
        {
            return canonicalUri.Contains("/data/scripts/story/", StringComparison.OrdinalIgnoreCase);
        }

        public string? ResolveDialogFile(string dialogName)
        {
            return null;
        }

        public IReadOnlyCollection<int> GetChapters(string canonicalUri)
        {
            return [];
        }
    }
}