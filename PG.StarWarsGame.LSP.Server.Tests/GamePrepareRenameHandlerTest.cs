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
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Xml;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class GamePrepareRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string LuaUri = "file:///script.lua";
    private const string TxtUri = "file:///data.txt";

    // ── helpers ────────────────────────────────────────────────────────────────

    private static PrepareRenameParams PrepareAt(string uri, int line = 0, int character = 0)
    {
        return new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character)
        };
    }

    private static GamePrepareRenameHandler BuildHandler(
        IXmlRenameProvider? xmlProvider = null,
        ILuaRenameProvider? luaProvider = null)
    {
        return new GamePrepareRenameHandler(
            new FakeIndexService(),
            xmlProvider ?? new NullXmlProvider(),
            luaProvider ?? new NullLuaProvider(),
            new FileHelper(new MockFileSystem()));
    }

    // ── routing ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownFileType_ReturnsNull()
    {
        var result = await BuildHandler().Handle(PrepareAt(TxtUri), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_XmlFile_RoutesToXmlProvider()
    {
        var range = new RangeOrPlaceholderRange(new Range());
        var result = await BuildHandler(new StubProvider(range))
            .Handle(PrepareAt(XmlUri), CancellationToken.None);
        Assert.Same(range, result);
    }

    [Fact]
    public async Task Handle_LuaFile_RoutesToLuaProvider()
    {
        var range = new RangeOrPlaceholderRange(new Range());
        var result = await BuildHandler(luaProvider: new StubProvider(range))
            .Handle(PrepareAt(LuaUri), CancellationToken.None);
        Assert.Same(range, result);
    }

    [Fact]
    public async Task Handle_XmlFile_XmlProviderReturnsNull_ReturnsNull()
    {
        var result = await BuildHandler(new NullXmlProvider())
            .Handle(PrepareAt(XmlUri), CancellationToken.None);
        Assert.Null(result);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
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

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
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

    private sealed class NullXmlProvider : IXmlRenameProvider
    {
        public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
        {
            return null;
        }

        public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
        {
            return null;
        }
    }

    private sealed class NullLuaProvider : ILuaRenameProvider
    {
        public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
        {
            return null;
        }

        public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
        {
            return null;
        }
    }

    private sealed class StubProvider : IXmlRenameProvider, ILuaRenameProvider
    {
        private readonly RangeOrPlaceholderRange _range;

        public StubProvider(RangeOrPlaceholderRange range)
        {
            _range = range;
        }

        public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
        {
            return null;
        }

        public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
        {
            return _range;
        }
    }
}