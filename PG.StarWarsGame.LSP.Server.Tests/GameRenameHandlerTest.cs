// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Testing;
using PG.StarWarsGame.LSP.Xml;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class GameRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string LuaUri = "file:///script.lua";
    private const string TxtUri = "file:///data.txt";

    // ── what we do not claim is the analyzer's (#154) ───────────────────────

    private const string ModScript = "file:///c:/mods/mymod/data/scripts/story/a.lua";
    private const string BaseScript = "file:///c:/games/eaw/data/scripts/library/pgbase.lua";

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
        ILuaRenameProvider? luaProvider = null,
        ILspConfigurationProvider? config = null,
        ILuaAnalyzer? analyzer = null,
        IProjectLayerMap? layers = null,
        ILuaSandboxProvider? sandbox = null)
    {
        return new GameRenameHandler(
            new FakeGameIndexService(),
            xmlProvider ?? new NullXmlProvider(),
            luaProvider ?? new NullLuaProvider(),
            new FileHelper(new MockFileSystem()),
            config ?? new FakeLspConfigurationProvider(),
            analyzer ?? new SilentLuaAnalyzer(),
            layers ?? new ProjectLayerMap(new FileHelper(new MockFileSystem())),
            sandbox ?? new EmptySandbox());
    }

    private static ProjectLayerMap Layers()
    {
        var map = new ProjectLayerMap(new FileHelper(new MockFileSystem()));
        map.SetLayers([
            new ProjectLayer(0, "EaW", [], ["c:/games/eaw/data/scripts"], [], [], null),
            new ProjectLayer(1, "Mod", [], ["c:/mods/mymod/data/scripts"], [], [], null)
        ]);
        return map;
    }

    private static WorkspaceEdit EditOf(params string[] uris)
    {
        return new WorkspaceEdit
        {
            Changes = uris.ToDictionary(u => DocumentUri.From(u), _ => (IEnumerable<TextEdit>)
            [
                new TextEdit
                {
                    NewText = "renamed",
                    Range = new Range(0, 0, 0, 1)
                }
            ])
        };
    }

    [Fact]
    public async Task NotOurs_TheAnalyzersRename()
    {
        var analyzer = new AnswerOnce("textDocument/rename", EditOf(ModScript));

        var result = await BuildHandler(luaProvider: new ClaimsNothing(), analyzer: analyzer, layers: Layers())
            .Handle(RenameAt(ModScript), CancellationToken.None);

        Assert.Equal([ModScript], result!.Changes!.Keys.Select(k => k.ToString()));
    }

    [Fact]
    public async Task Ours_TheAnalyzerIsNotAsked_EvenWhenWeRefuse()
    {
        var analyzer = new AnswerOnce("textDocument/rename", EditOf(ModScript));

        var result = await BuildHandler(luaProvider: new NullLuaProvider(), analyzer: analyzer, layers: Layers())
            .Handle(RenameAt(ModScript), CancellationToken.None);

        Assert.Null(result);
        Assert.False(analyzer.Asked);
    }

    /// <summary>The analyzer reads lower layers and the stubs as library; this server never edits them.</summary>
    [Fact]
    public async Task TheAnalyzersRename_ReachingOutsideTheProject_IsRefused()
    {
        var analyzer = new AnswerOnce("textDocument/rename", EditOf(ModScript, BaseScript));
        var handler = BuildHandler(luaProvider: new ClaimsNothing(), analyzer: analyzer, layers: Layers());

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(RenameAt(ModScript), CancellationToken.None));

        Assert.StartsWith("Rename refused: ", refusal.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, int> SetupsAndRanks()
    {
        var data = new TheoryData<string, int>();
        foreach (var (name, rank) in ProjectSetup.NamesAndRanks) data.Add(name, rank);
        return data;
    }

    // Every shape, the second edit in every layer (#280): within the leaf the rename goes through;
    // an edit to any lower layer's file refuses it whole.
    [Theory]
    [MemberData(nameof(SetupsAndRanks))]
    public async Task TheAnalyzersRename_InEveryShape_GoesThroughOnlyInsideTheLeaf(string name, int rank)
    {
        var setup = ProjectSetup.Named(name);
        var layers = new ProjectLayerMap(new FileHelper(new MockFileSystem()));
        layers.SetLayers(setup.Layers);
        var mine = setup.ScriptUri(setup.LeafRank, "Story/A.lua");
        var other = setup.ScriptUri(rank, "Library/PGBase.lua");
        var handler = BuildHandler(luaProvider: new ClaimsNothing(),
            analyzer: new AnswerOnce("textDocument/rename", EditOf(mine, other)), layers: layers);

        if (rank != setup.LeafRank)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(RenameAt(mine), CancellationToken.None));
            return;
        }

        var result = await handler.Handle(RenameAt(mine), CancellationToken.None);
        Assert.Equal(2, result!.Changes!.Count);
    }

    /// <summary>Two story scripts are two states: renaming a global in one leaves the other alone (#249).</summary>
    [Fact]
    public async Task TheAnalyzersRename_InAnotherScriptsState_IsDropped()
    {
        const string otherScript = "file:///c:/mods/mymod/data/scripts/story/b.lua";
        var documents = ImmutableDictionary.CreateRange(new[]
        {
            KeyValuePair.Create(ModScript, new DocumentIndex(ModScript, 1, [], [])),
            KeyValuePair.Create(otherScript, new DocumentIndex(otherScript, 1, [], []))
        });
        var sandbox = new OneSandbox(LuaSandbox.Build(documents, new FileHelper(new MockFileSystem())));
        var analyzer = new AnswerOnce("textDocument/rename", EditOf(ModScript, otherScript));

        var result = await BuildHandler(luaProvider: new ClaimsNothing(), analyzer: analyzer, layers: Layers(),
            sandbox: sandbox).Handle(RenameAt(ModScript), CancellationToken.None);

        Assert.Equal([ModScript], result!.Changes!.Keys.Select(k => k.ToString()));
    }

    /// <summary>A library both scripts load names the global: the rename is every such script's.</summary>
    [Fact]
    public async Task TheAnalyzersRename_ThroughASharedLibrary_KeepsEveryScriptLoadingIt()
    {
        const string otherScript = "file:///c:/mods/mymod/data/scripts/story/b.lua";
        const string library = "file:///c:/mods/mymod/data/scripts/library/mylib.lua";
        var documents = ImmutableDictionary.CreateRange(new[]
        {
            KeyValuePair.Create(ModScript, new DocumentIndex(ModScript, 1, [], [], ["MyLib"])),
            KeyValuePair.Create(otherScript, new DocumentIndex(otherScript, 1, [], [], ["MyLib"])),
            KeyValuePair.Create(library, new DocumentIndex(library, 1, [], []))
        });
        var sandbox = new OneSandbox(LuaSandbox.Build(documents, new FileHelper(new MockFileSystem())));
        var analyzer = new AnswerOnce("textDocument/rename", EditOf(ModScript, otherScript, library));

        var result = await BuildHandler(luaProvider: new ClaimsNothing(), analyzer: analyzer, layers: Layers(),
            sandbox: sandbox).Handle(RenameAt(ModScript), CancellationToken.None);

        Assert.Equal(3, result!.Changes!.Count);
    }

    private sealed class OneSandbox(LuaSandbox sandbox) : ILuaSandboxProvider
    {
        public LuaSandbox Current => sandbox;
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
        var result = await BuildHandler(new StubProvider(xmlEdit))
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
        var result = await BuildHandler(new NullXmlProvider())
            .Handle(RenameAt(XmlUri), CancellationToken.None);
        Assert.Null(result);
    }

    private sealed class ClaimsNothing : ILuaRenameProvider
    {
        public WorkspaceEdit HandleRename(string uri, RenameParams request, GameIndex index)
        {
            throw new InvalidOperationException("not claimed, never asked");
        }

        public RangeOrPlaceholderRange HandlePrepare(string uri, int line, int character, GameIndex index)
        {
            throw new InvalidOperationException("not claimed, never asked");
        }

        public bool Claims(string uri, int line, int character, GameIndex index)
        {
            return false;
        }
    }

    internal sealed class AnswerOnce(string method, object answer) : ILuaAnalyzer
    {
        public bool Asked { get; private set; }

        public bool IsRunning => true;

        public event Action<PublishDiagnosticsParams>? DiagnosticsPublished
        {
            add { }
            remove { }
        }

        public void DidOpen(string uri, string text, int version)
        {
        }

        public void DidChange(string uri, string text, int version)
        {
        }

        public void DidClose(string uri)
        {
        }

        public Task<T?> RequestAsync<T>(string asked, object parameters, CancellationToken ct) where T : class?
        {
            Asked = true;
            return Task.FromResult(asked == method ? answer as T : null);
        }
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

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
        private readonly WorkspaceEdit _edit;

        public StubProvider(WorkspaceEdit edit)
        {
            _edit = edit;
        }

        public WorkspaceEdit HandleRename(string uri, RenameParams request, GameIndex index)
        {
            return _edit;
        }

        public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
        {
            return null;
        }
    }
}