// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class GameDidChangeWatchedFilesHandlerTest
{
    private const string XmlUri = "file:///c:/data/units.xml";
    private const string LuaUri = "file:///c:/scripts/mission.lua";

    // ── helpers ────────────────────────────────────────────────────────────────

    private static DidChangeWatchedFilesParams Changed(params string[] uris)
    {
        return new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(uris.Select(u =>
                new FileEvent { Uri = DocumentUri.From(u), Type = FileChangeType.Changed }))
        };
    }

    private static DidChangeWatchedFilesParams Deleted(params string[] uris)
    {
        return new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(uris.Select(u =>
                new FileEvent { Uri = DocumentUri.From(u), Type = FileChangeType.Deleted }))
        };
    }

    private static GameDidChangeWatchedFilesHandler BuildHandler(
        SpyIndexService? index = null,
        FakeWorkspaceHost? host = null,
        MockFileSystem? fs = null)
    {
        var fileSystem = fs ?? new MockFileSystem();
        return new GameDidChangeWatchedFilesHandler(
            index ?? new SpyIndexService(),
            host ?? new FakeWorkspaceHost(),
            new FileHelper(fileSystem),
            NullLogger<GameDidChangeWatchedFilesHandler>.Instance);
    }

    // ── changed file not open in editor ──────────────────────────────────────

    [Fact]
    public async Task Handle_ChangedFile_NotInWorkspaceHost_ReIndexesFromDisk()
    {
        const string path = @"c:\data\units.xml";
        const string freshContent = "<GameObjectFiles><Unit Name=\"NEW_UNIT\"/></GameObjectFiles>";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new(freshContent)
        });
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy, fs: fs);
        await handler.Handle(Changed(XmlUri), CancellationToken.None);

        Assert.Contains(spy.Updates, u => u.Uri == "file:///c:/data/units.xml" && u.Text == freshContent);
    }

    [Fact]
    public async Task Handle_ChangedFile_OpenInEditor_DoesNotReIndex()
    {
        const string path = @"c:\scripts\mission.lua";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new("-- stale disk content")
        });
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "-- live editor content", 5);
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy, host, fs);
        await handler.Handle(Changed(LuaUri), CancellationToken.None);

        Assert.Empty(spy.Updates);
    }

    // ── deleted file ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DeletedFile_RemovesFromIndex()
    {
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy);
        await handler.Handle(Deleted(XmlUri), CancellationToken.None);

        Assert.Contains(spy.Removals, r => r == "file:///c:/data/units.xml");
        Assert.Empty(spy.Updates);
    }

    // ── file not found on disk ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ChangedFile_NotOnDisk_IsIgnored()
    {
        var spy = new SpyIndexService();

        var handler = BuildHandler(spy, fs: new MockFileSystem()); // empty fs
        await handler.Handle(Changed(XmlUri), CancellationToken.None);

        Assert.Empty(spy.Updates);
        Assert.Empty(spy.Removals);
    }

    // ── multiple changes ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MultipleChanges_AllClosedFilesReIndexed()
    {
        const string xmlPath = @"c:\data\units.xml";
        const string luaPath = @"c:\scripts\mission.lua";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [xmlPath] = new("<root/>"),
            [luaPath] = new("-- script")
        });
        var spy = new SpyIndexService();

        var request = new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(
                new FileEvent { Uri = DocumentUri.From(XmlUri), Type = FileChangeType.Changed },
                new FileEvent { Uri = DocumentUri.From(LuaUri), Type = FileChangeType.Changed })
        };
        var handler = BuildHandler(spy, fs: fs);
        await handler.Handle(request, CancellationToken.None);

        Assert.Equal(2, spy.Updates.Count);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class SpyIndexService : IGameIndexService
    {
        public readonly List<string> Removals = [];

        public readonly List<(string Uri, string Text)> Updates = [];
        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            Updates.Add((uri, text));
            return Task.CompletedTask;
        }

        public void RemoveDocument(string uri)
        {
            Removals.Add(uri);
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version)
        {
            _docs[uri] = new TrackedDocument(uri, text, version);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }
}