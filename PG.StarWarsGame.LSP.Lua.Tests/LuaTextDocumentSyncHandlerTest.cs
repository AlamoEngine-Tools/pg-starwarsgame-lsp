// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaTextDocumentSyncHandlerTest
{
    private const string DiskUri = "file:///c:/scripts/test.lua";
    private const string DiskPath = @"c:\scripts\test.lua";
    private const string DiskContent = "function Definitions() end";
    private static DocumentUri TestUri => DocumentUri.From("file:///test.lua");

    private static (LuaTextDocumentSyncHandler handler,
        FakeGameWorkspaceHost host,
        FakeGameIndexService index) Build(MockFileSystem? fs = null)
    {
        var host = new FakeGameWorkspaceHost();
        var index = new FakeGameIndexService();
        var fileHelper = new FileHelper(fs ?? new MockFileSystem());
        return (new LuaTextDocumentSyncHandler(host, index, fileHelper), host, index);
    }

    // ── DidOpen ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidOpen_Adds_Document_To_WorkspaceHost()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "function Foo() end", LanguageId = "lua", Version = 1 }
        }, CancellationToken.None);

        Assert.Single(host.AddOrUpdateCalls);
        Assert.Equal(TestUri.ToString(), host.AddOrUpdateCalls[0].Uri);
        Assert.Equal("function Foo() end", host.AddOrUpdateCalls[0].Text);
        Assert.Equal(1, host.AddOrUpdateCalls[0].Version);
    }

    [Fact]
    public async Task DidOpen_Triggers_Index_Update()
    {
        var (handler, _, index) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "function Foo() end", LanguageId = "lua", Version = 1 }
        }, CancellationToken.None);

        Assert.Single(index.UpdateCalls);
        Assert.Equal(TestUri.ToString(), index.UpdateCalls[0].Uri);
        Assert.Equal("function Foo() end", index.UpdateCalls[0].Text);
        Assert.Equal(1, index.UpdateCalls[0].Version);
    }

    // ── DidChange ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidChange_Updates_WorkspaceHost_With_New_Text()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
                { Uri = TestUri, Version = 2 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "function Bar() end" })
        }, CancellationToken.None);

        Assert.Single(host.AddOrUpdateCalls);
        Assert.Equal("function Bar() end", host.AddOrUpdateCalls[0].Text);
        Assert.Equal(2, host.AddOrUpdateCalls[0].Version);
    }

    [Fact]
    public async Task DidChange_Triggers_Index_Update()
    {
        var (handler, _, index) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
                { Uri = TestUri, Version = 3 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "function Changed() end" })
        }, CancellationToken.None);

        Assert.Single(index.UpdateCalls);
        Assert.Equal("function Changed() end", index.UpdateCalls[0].Text);
        Assert.Equal(3, index.UpdateCalls[0].Version);
    }

    [Fact]
    public async Task DidChange_EmptyContentChanges_Uses_EmptyString()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = TestUri },
            ContentChanges = new Container<TextDocumentContentChangeEvent>()
        }, CancellationToken.None);

        Assert.Equal(string.Empty, host.AddOrUpdateCalls[0].Text);
    }

    // ── DidClose ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidClose_Removes_Document_From_WorkspaceHost()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new(DiskContent) });
        var (handler, host, _) = Build(fs);

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Single(host.RemoveCalls);
        Assert.Equal(DiskUri, host.RemoveCalls[0]);
    }

    [Fact]
    public async Task DidClose_WhenFileExistsOnDisk_ReindexesAtVersionZero()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new(DiskContent) });
        var (handler, _, index) = Build(fs);

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Single(index.RemoveCalls);
        Assert.Single(index.UpdateCalls);
        Assert.Equal(DiskContent, index.UpdateCalls[0].Text);
        Assert.Equal(0, index.UpdateCalls[0].Version);
        Assert.Equal(1, index.BulkUpdateCount);
    }

    [Fact]
    public async Task DidClose_WhenFileNotOnDisk_RemovesFromIndex()
    {
        var (handler, _, index) = Build(new MockFileSystem());

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Single(index.RemoveCalls);
        Assert.Empty(index.UpdateCalls);
    }

    // ── DidSave ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidSave_Does_Not_Touch_Host_Or_Index()
    {
        var (handler, host, index) = Build();

        await handler.Handle(new DidSaveTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri }
        }, CancellationToken.None);

        Assert.Empty(host.AddOrUpdateCalls);
        Assert.Empty(index.UpdateCalls);
    }

    // ── GetTextDocumentAttributes ─────────────────────────────────────────────

    [Fact]
    public void GetTextDocumentAttributes_ReturnsLuaLanguage()
    {
        var (handler, _, _) = Build();
        var attrs = handler.GetTextDocumentAttributes(TestUri);
        Assert.Equal("lua", attrs.LanguageId);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    internal sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        public List<Call> AddOrUpdateCalls { get; } = [];
        public List<string> RemoveCalls { get; } = [];

        public void AddOrUpdate(string uri, string text, int version)
        {
            AddOrUpdateCalls.Add(new Call(uri, text, version));
        }

        public void Remove(string uri)
        {
            RemoveCalls.Add(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            doc = null!;
            return false;
        }

        public IEnumerable<TrackedDocument> All => [];

        public record Call(string Uri, string Text, int Version);
    }

    internal sealed class FakeGameIndexService : IGameIndexService
    {
        public List<UpdateCall> UpdateCalls { get; } = [];
        public List<string> RemoveCalls { get; } = [];
        public int BulkUpdateCount { get; private set; }

        public GameIndex Current => GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            UpdateCalls.Add(new UpdateCall(uri, text, version));
            return Task.CompletedTask;
        }

        public void RemoveDocument(string uri)
        {
            RemoveCalls.Add(uri);
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(
            System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableArray<string>> bones)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            BulkUpdateCount++;
            return NullDisposable.Instance;
        }

        public record UpdateCall(string Uri, string Text, int Version);

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}