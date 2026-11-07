// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Testing;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests;

/// <summary>
///     What the Lua rename does not claim (#154) - a local, a field - is the analyzer's to rename,
///     within the script's own state (#249) and within the project only (#280). The provider owns
///     that hand-over since #312; the server's rename handler knows nothing of it.
/// </summary>
public sealed class LuaRenameHandlerAnalyzerFallbackTest
{
    private const string ModScript = "file:///c:/mods/mymod/data/scripts/story/a.lua";
    private const string BaseScript = "file:///c:/games/eaw/data/scripts/library/pgbase.lua";
    private const string Stub = "file:///c:/schema/lua/api.d.lua";

    /// <summary>The cursor at (1, 7) sits on the local - nothing this server renames.</summary>
    private const string LocalScript = "local abc = 1\nprint(abc)";

    private static RenameParams RenameAt(string uri, int line = 1, int character = 7, string newName = "NEW")
    {
        return new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character),
            NewName = newName
        };
    }

    private static PrepareRenameParams PrepareAt(string uri, int line = 1, int character = 7)
    {
        return new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character)
        };
    }

    private static LuaRenameHandler Handler(ScriptedLuaAnalyzer analyzer, IProjectLayerMap? layers = null,
        ILuaSandboxProvider? sandbox = null, params (string Uri, string Text)[] scripts)
    {
        var host = new FakeGameWorkspaceHost();
        foreach (var (uri, text) in scripts) host.AddOrUpdate(uri, text, 1);
        var fileHelper = new FileHelper(new MockFileSystem());
        var textSource = new DocumentTextSource(host, fileHelper, NullLogger<DocumentTextSource>.Instance);
        return new LuaRenameHandler(
            new LuaParseCache(textSource, 16, NullLogger<LuaParseCache>.Instance),
            textSource,
            new SchemaProviderBuilder().Build(),
            NullLogger<LuaRenameHandler>.Instance,
            new LuaApiSchemaProvider([]),
            sandbox ?? FixedSandbox.Empty(),
            fileHelper,
            new FakeGameIndexService(),
            analyzer,
            layers ?? new ProjectLayerMap(fileHelper),
            TestCatalogues.English,
            new LuaStubLocation { Directory = "c:/schema/lua" });
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
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/rename", EditOf(ModScript));
        var handler = Handler(analyzer, Layers(), scripts: [(ModScript, LocalScript)]);

        var result = await handler.RenameAsync(RenameAt(ModScript), CancellationToken.None);

        Assert.Equal([ModScript], result!.Changes!.Keys.Select(k => k.ToString()));
    }

    [Fact]
    public async Task NotOurs_TheAnalyzerPrepares()
    {
        var range = new RangeOrPlaceholderRange(new Range(1, 6, 1, 9));
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/prepareRename", range);
        var handler = Handler(analyzer, scripts: [(ModScript, LocalScript)]);

        var result = await handler.PrepareAsync(PrepareAt(ModScript), CancellationToken.None);

        Assert.Same(range, result);
    }

    // An engine member - fett.Move_To - is no name of ours: the analyzer that renames it edits the
    // stub declaring it, and the engine binds it, so a rename breaks every script calling it.
    [Fact]
    public async Task NotOurs_ANameWhoseRenameEditsTheStubs_IsNotPrepared()
    {
        var analyzer = new ScriptedLuaAnalyzer()
            .Answer("textDocument/prepareRename", new RangeOrPlaceholderRange(new Range(1, 6, 1, 9)))
            .Answer("textDocument/rename", EditOf(ModScript, Stub));
        var handler = Handler(analyzer, scripts: [(ModScript, LocalScript)]);

        var result = await handler.PrepareAsync(PrepareAt(ModScript), CancellationToken.None);

        Assert.Null(result);
    }

    // The analyzer's rename of an engine member edits the stub and every script calling it; the
    // scripts of other states go, and the stub must not go with them unnoticed.
    [Fact]
    public async Task TheAnalyzersRename_EditingTheStubs_IsRefused_BeforeTheStateIsKept()
    {
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/rename", EditOf(ModScript, Stub));
        var handler = Handler(analyzer, Layers(), scripts: [(ModScript, LocalScript)]);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.RenameAsync(RenameAt(ModScript), CancellationToken.None));

        Assert.Equal("Rename refused: It edits api.d.lua, which is not one of this project's scripts", refusal.Message);
    }

    /// <summary>Nothing parsed is still ours: the refusal stands and the analyzer is not asked.</summary>
    [Fact]
    public async Task Ours_TheAnalyzerIsNotAsked_EvenWhenWeRefuse()
    {
        var analyzer = new ScriptedLuaAnalyzer()
            .Answer("textDocument/rename", EditOf(ModScript))
            .Answer("textDocument/prepareRename", new RangeOrPlaceholderRange(new Range()));
        var handler = Handler(analyzer, Layers());

        var edit = await handler.RenameAsync(RenameAt(ModScript), CancellationToken.None);
        var range = await handler.PrepareAsync(PrepareAt(ModScript), CancellationToken.None);

        Assert.Null(edit);
        Assert.Null(range);
        Assert.Empty(analyzer.Requests);
    }

    /// <summary>The analyzer reads lower layers and the stubs as library; this server never edits them.</summary>
    [Fact]
    public async Task TheAnalyzersRename_ReachingOutsideTheProject_IsRefused()
    {
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/rename", EditOf(ModScript, BaseScript));
        var handler = Handler(analyzer, Layers(), scripts: [(ModScript, LocalScript)]);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.RenameAsync(RenameAt(ModScript), CancellationToken.None));

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
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/rename", EditOf(mine, other));
        var handler = Handler(analyzer, layers, scripts: [(mine, LocalScript)]);

        if (rank != setup.LeafRank)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.RenameAsync(RenameAt(mine), CancellationToken.None));
            return;
        }

        var result = await handler.RenameAsync(RenameAt(mine), CancellationToken.None);
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
        var sandbox = new FixedSandbox(LuaSandbox.Build(documents, new FileHelper(new MockFileSystem())));
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/rename", EditOf(ModScript, otherScript));
        var handler = Handler(analyzer, Layers(), sandbox, (ModScript, LocalScript));

        var result = await handler.RenameAsync(RenameAt(ModScript), CancellationToken.None);

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
        var sandbox = new FixedSandbox(LuaSandbox.Build(documents, new FileHelper(new MockFileSystem())));
        var analyzer = new ScriptedLuaAnalyzer()
            .Answer("textDocument/rename", EditOf(ModScript, otherScript, library));
        var handler = Handler(analyzer, Layers(), sandbox, (ModScript, LocalScript));

        var result = await handler.RenameAsync(RenameAt(ModScript), CancellationToken.None);

        Assert.Equal(3, result!.Changes!.Count);
    }
}