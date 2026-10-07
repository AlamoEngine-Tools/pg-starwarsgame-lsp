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
        ILuaRenameProvider? luaProvider = null,
        ILspConfigurationProvider? config = null,
        ILuaAnalyzer? analyzer = null,
        IProjectLayerMap? layers = null)
    {
        return new GameRenameHandler(
            new FakeIndexService(),
            xmlProvider ?? new NullXmlProvider(),
            luaProvider ?? new NullLuaProvider(),
            new FileHelper(new MockFileSystem()),
            config ?? new FakeLspConfigurationProvider(),
            analyzer,
            layers);
    }

    // ── what we do not claim is the analyzer's (#154) ───────────────────────

    private const string ModScript = "file:///c:/mods/mymod/data/scripts/story/a.lua";
    private const string BaseScript = "file:///c:/games/eaw/data/scripts/library/pgbase.lua";

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
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(0, 0, 0, 1)
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

    private sealed class ClaimsNothing : ILuaRenameProvider
    {
        public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
        {
            throw new InvalidOperationException("not claimed, never asked");
        }

        public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
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

        public Task<T?> RequestAsync<T>(string asked, object parameters, CancellationToken ct) where T : class
        {
            Asked = true;
            return Task.FromResult(asked == method ? answer as T : null);
        }
    }

    // ── feature flags ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_XmlRenameFlagOff_ReturnsNullWithoutInvokingXmlProvider()
    {
        var stub = new StubProvider(new WorkspaceEdit());
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Xml = new XmlFeatureFlags { Rename = false } });

        var result = await BuildHandler(stub, config: config)
            .Handle(RenameAt(XmlUri), CancellationToken.None);

        Assert.Null(result);
        Assert.False(stub.RenameCalled);
    }

    [Fact]
    public async Task Handle_XmlRenameFlagOff_LuaStillRouted()
    {
        var luaEdit = new WorkspaceEdit();
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Xml = new XmlFeatureFlags { Rename = false } });

        var result = await BuildHandler(luaProvider: new StubProvider(luaEdit), config: config)
            .Handle(RenameAt(LuaUri), CancellationToken.None);

        Assert.Same(luaEdit, result);
    }

    [Fact]
    public async Task Handle_LuaRenameFlagOff_ReturnsNullWithoutInvokingLuaProvider()
    {
        var stub = new StubProvider(new WorkspaceEdit());
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Lua = new LuaFeatureFlags { Rename = false } });

        var result = await BuildHandler(luaProvider: stub, config: config)
            .Handle(RenameAt(LuaUri), CancellationToken.None);

        Assert.Null(result);
        Assert.False(stub.RenameCalled);
    }

    [Fact]
    public async Task Handle_LuaRenameFlagOff_XmlStillRouted()
    {
        var xmlEdit = new WorkspaceEdit();
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Lua = new LuaFeatureFlags { Rename = false } });

        var result = await BuildHandler(new StubProvider(xmlEdit), config: config)
            .Handle(RenameAt(XmlUri), CancellationToken.None);

        Assert.Same(xmlEdit, result);
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

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; } = GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

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

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
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
        private readonly WorkspaceEdit _edit;

        public StubProvider(WorkspaceEdit edit)
        {
            _edit = edit;
        }

        public bool RenameCalled { get; private set; }

        public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
        {
            RenameCalled = true;
            return _edit;
        }

        public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
        {
            return null;
        }
    }
}