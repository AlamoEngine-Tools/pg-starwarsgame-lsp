// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Story.Dialog;

namespace PG.StarWarsGame.LSP.Story.Tests.Dialog;

public sealed class DialogTextDocumentSyncHandlerTest
{
    private const string InScopeUri = "file:///ws/data/scripts/story/dialog_test.txt";
    private const string OutOfScopeUri = "file:///ws/notes.txt";

    private static (DialogTextDocumentSyncHandler Handler,
        DialogDiagnosticsPublisherTest.FakeWorkspaceHost Host,
        SpyRevalidator Revalidator) Build(bool scopeEnabled = true)
    {
        var host = new DialogDiagnosticsPublisherTest.FakeWorkspaceHost();
        var revalidator = new SpyRevalidator();
        var gate = new StartupGate();
        gate.OpenAsync().GetAwaiter().GetResult();
        var handler = new DialogTextDocumentSyncHandler(
            host,
            new FileHelper(new MockFileSystem()),
            gate,
            new DialogDiagnosticsPublisherTest.FakeDialogScope { Enabled = scopeEnabled },
            revalidator);
        return (handler, host, revalidator);
    }

    private static DidOpenTextDocumentParams Open(string uri, string text = "[CHAPTER 0]")
    {
        return new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = DocumentUri.From(uri), LanguageId = "plaintext", Version = 1, Text = text }
        };
    }

    [Fact]
    public async Task DidOpen_InScopeTxt_TracksAndRevalidates()
    {
        var (handler, host, revalidator) = Build();

        await handler.Handle(Open(InScopeUri), CancellationToken.None);

        Assert.True(host.TryGet(InScopeUri, out _));
        Assert.Equal([InScopeUri], revalidator.Revalidated);
    }

    [Fact]
    public async Task DidOpen_OutOfScopeTxt_IsIgnored()
    {
        var (handler, host, revalidator) = Build();

        await handler.Handle(Open(OutOfScopeUri), CancellationToken.None);

        Assert.False(host.TryGet(OutOfScopeUri, out _));
        Assert.Empty(revalidator.Revalidated);
    }

    [Fact]
    public async Task DidOpen_NonTxtDocument_IsIgnored()
    {
        var (handler, host, revalidator) = Build();

        await handler.Handle(Open("file:///ws/data/scripts/story/script.lua"), CancellationToken.None);

        Assert.False(host.TryGet("file:///ws/data/scripts/story/script.lua", out _));
        Assert.Empty(revalidator.Revalidated);
    }

    [Fact]
    public async Task DidOpen_ScopeDisabled_IsIgnored()
    {
        var (handler, host, revalidator) = Build(false);

        await handler.Handle(Open(InScopeUri), CancellationToken.None);

        Assert.False(host.TryGet(InScopeUri, out _));
        Assert.Empty(revalidator.Revalidated);
    }

    [Fact]
    public async Task DidChange_UpdatesTextAndRevalidates()
    {
        var (handler, host, revalidator) = Build();
        await handler.Handle(Open(InScopeUri), CancellationToken.None);

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
                { Uri = DocumentUri.From(InScopeUri), Version = 2 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "[CHAPTER 1]" })
        }, CancellationToken.None);

        Assert.True(host.TryGet(InScopeUri, out var doc));
        Assert.Equal("[CHAPTER 1]", doc.Text);
        Assert.Equal(2, revalidator.Revalidated.Count);
    }

    [Fact]
    public async Task DidClose_TrackedDocument_RemovesAndClears()
    {
        var (handler, host, revalidator) = Build();
        await handler.Handle(Open(InScopeUri), CancellationToken.None);

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(InScopeUri) }
        }, CancellationToken.None);

        Assert.False(host.TryGet(InScopeUri, out _));
        Assert.Equal([InScopeUri], revalidator.Cleared);
    }

    [Fact]
    public async Task DidClose_UntrackedDocument_DoesNotClear()
    {
        var (handler, _, revalidator) = Build();

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(OutOfScopeUri) }
        }, CancellationToken.None);

        Assert.Empty(revalidator.Cleared);
    }

    private sealed class SpyRevalidator : IDialogDiagnosticsRevalidator
    {
        public List<string> Revalidated { get; } = [];
        public List<string> Cleared { get; } = [];

        public Task RevalidateDocumentAsync(string uri, CancellationToken ct)
        {
            Revalidated.Add(uri);
            return Task.CompletedTask;
        }

        public void ClearDocument(string uri)
        {
            Cleared.Add(uri);
        }
    }
}