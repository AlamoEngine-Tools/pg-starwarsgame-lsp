// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class GameRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string LuaUri = "file:///script.lua";
    private const string TxtUri = "file:///data.txt";

    // ── helpers ────────────────────────────────────────────────────────────────

    private static RenameParams RenameAt(string uri, int line = 0, int character = 0, string newName = "NEW")
    {
        return new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character),
            NewName = newName
        };
    }

    private static GameRenameHandler BuildHandler(
        IXmlRenameProvider? xmlProvider = null,
        ILuaRenameProvider? luaProvider = null)
    {
        return new GameRenameHandler(
            new FakeIndexService(),
            xmlProvider ?? new NullXmlProvider(),
            luaProvider ?? new NullLuaProvider(),
            new FileHelper(new MockFileSystem()));
    }

    // ── routing ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownFileType_ReturnsNull()
    {
        var result = await BuildHandler().Handle(RenameAt(TxtUri), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_XmlFile_RoutesToXmlProvider()
    {
        var xmlEdit = new WorkspaceEdit();
        var result = await BuildHandler(xmlProvider: new StubProvider(xmlEdit))
            .Handle(RenameAt(XmlUri), CancellationToken.None);
        Assert.Same(xmlEdit, result);
    }

    [Fact]
    public async Task Handle_LuaFile_RoutesToLuaProvider()
    {
        var luaEdit = new WorkspaceEdit();
        var result = await BuildHandler(luaProvider: new StubProvider(luaEdit))
            .Handle(RenameAt(LuaUri), CancellationToken.None);
        Assert.Same(luaEdit, result);
    }

    [Fact]
    public async Task Handle_XmlFile_XmlProviderReturnsNull_ReturnsNull()
    {
        var result = await BuildHandler(xmlProvider: new NullXmlProvider())
            .Handle(RenameAt(XmlUri), CancellationToken.None);
        Assert.Null(result);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;
        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct) => Task.CompletedTask;
        public void RemoveDocument(string uri) { }
        public void ApplyBaseline(BaselineIndex baseline) { }
        public void ApplyLocalisation(ILocalisationIndex index) { }
        public void ApplyAssetFiles(IAssetFileIndex index) { }
        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones) { }
        public IDisposable BeginBulkUpdate() => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class NullXmlProvider : IXmlRenameProvider
    {
        public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index) => null;
        public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index) => null;
    }

    private sealed class NullLuaProvider : ILuaRenameProvider
    {
        public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index) => null;
        public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index) => null;
    }

    private sealed class StubProvider : IXmlRenameProvider, ILuaRenameProvider
    {
        private readonly WorkspaceEdit _edit;
        public StubProvider(WorkspaceEdit edit) => _edit = edit;
        public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index) => _edit;
        public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index) => null;
    }
}
