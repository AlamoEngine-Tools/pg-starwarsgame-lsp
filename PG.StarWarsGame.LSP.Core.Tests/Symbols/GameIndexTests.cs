using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class GameIndexTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static GameSymbol Symbol(string id, string typeName = "Unit") =>
        new(id, GameSymbolKind.XmlObject, typeName, new FileOrigin("file:///f.xml", 1, null), null);

    private static BaselineIndex Baseline(params GameSymbol[] symbols) =>
        new(symbols.ToImmutableDictionary(s => s.Id),
            DateTimeOffset.UtcNow,
            "hash-abc");

    private static GameIndex WithBaseline(params GameSymbol[] symbols) =>
        GameIndex.Empty with { Baseline = Baseline(symbols) };

    private static GameIndex WithWorkspace(params GameSymbol[] symbols) =>
        GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols
                .GroupBy(s => s.Id)
                .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray())
        };

    private static GameIndex WithBoth(GameSymbol[] baselineSymbols, GameSymbol[] workspaceSymbols) =>
        GameIndex.Empty with
        {
            Baseline = Baseline(baselineSymbols),
            WorkspaceDefinitions = workspaceSymbols
                .GroupBy(s => s.Id)
                .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray())
        };

    // ── BaselineIndex ────────────────────────────────────────────────────────

    [Fact]
    public void BaselineIndex_Empty_Has_No_Symbols()
    {
        Assert.Empty(BaselineIndex.Empty.Symbols);
        Assert.Equal(DateTimeOffset.MinValue, BaselineIndex.Empty.BuiltAt);
        Assert.Equal(string.Empty, BaselineIndex.Empty.SourceManifestHash);
    }

    [Fact]
    public void BaselineIndex_Stores_Symbols_By_Id()
    {
        var sym = Symbol("UNIT_A");
        var index = Baseline(sym);
        Assert.True(index.Symbols.ContainsKey("UNIT_A"));
    }

    // ── GameIndex.Empty ──────────────────────────────────────────────────────

    [Fact]
    public void GameIndex_Empty_Has_Empty_Collections()
    {
        var index = GameIndex.Empty;
        Assert.Empty(index.Baseline.Symbols);
        Assert.Empty(index.Documents);
        Assert.Empty(index.WorkspaceDefinitions);
        Assert.Empty(index.WorkspaceReferences);
    }

    // ── Resolve ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Returns_Null_For_Unknown_Id()
    {
        Assert.Null(GameIndex.Empty.Resolve("NOPE"));
    }

    [Fact]
    public void Resolve_Returns_Baseline_Symbol_When_No_Workspace_Entry()
    {
        var sym = Symbol("UNIT_A");
        var index = WithBaseline(sym);
        Assert.Equal(sym, index.Resolve("UNIT_A"));
    }

    [Fact]
    public void Resolve_Returns_Workspace_Symbol_When_Present()
    {
        var ws = Symbol("UNIT_A");
        var index = WithWorkspace(ws);
        Assert.Equal(ws, index.Resolve("UNIT_A"));
    }

    [Fact]
    public void Resolve_Prefers_Workspace_Over_Baseline()
    {
        var baseline = Symbol("UNIT_A");
        var workspace = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///mod/units.xml", 5, null), null);

        var index = WithBoth([baseline], [workspace]);
        Assert.Equal(workspace, index.Resolve("UNIT_A"));
    }

    // ── ResolveAll ───────────────────────────────────────────────────────────

    [Fact]
    public void ResolveAll_Returns_Empty_For_Unknown_Id()
    {
        Assert.Empty(GameIndex.Empty.ResolveAll("NOPE"));
    }

    [Fact]
    public void ResolveAll_Returns_Baseline_Symbol_Only_When_No_Workspace()
    {
        var sym = Symbol("UNIT_A");
        var index = WithBaseline(sym);
        Assert.Equal([sym], index.ResolveAll("UNIT_A").ToList());
    }

    [Fact]
    public void ResolveAll_Returns_Workspace_Then_Baseline()
    {
        var baseline = Symbol("UNIT_A");
        var workspace = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///mod/units.xml", 5, null), null);

        var index = WithBoth([baseline], [workspace]);
        var results = index.ResolveAll("UNIT_A").ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(workspace, results[0]);
        Assert.Equal(baseline, results[1]);
    }

    [Fact]
    public void ResolveAll_Returns_All_Workspace_Entries_For_Duplicate_Id()
    {
        var ws1 = new GameSymbol("DUP", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///a.xml", 1, null), null);
        var ws2 = new GameSymbol("DUP", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///b.xml", 1, null), null);

        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("DUP", ImmutableArray.Create(ws1, ws2))
        };

        var results = index.ResolveAll("DUP").ToList();
        Assert.Equal(2, results.Count);
    }

    // ── ResolveWithShadow ────────────────────────────────────────────────────

    [Fact]
    public void ResolveWithShadow_Returns_Null_For_Unknown_Id()
    {
        Assert.Null(GameIndex.Empty.ResolveWithShadow("NOPE"));
    }

    [Fact]
    public void ResolveWithShadow_Returns_Null_Shadowed_When_No_Baseline_Entry()
    {
        var ws = Symbol("UNIT_A");
        var index = WithWorkspace(ws);
        var result = index.ResolveWithShadow("UNIT_A");

        Assert.NotNull(result);
        Assert.Equal(ws, result.Value.Winner);
        Assert.Null(result.Value.Shadowed);
    }

    [Fact]
    public void ResolveWithShadow_Returns_Shadowed_When_Workspace_Overrides_Baseline()
    {
        var baseline = Symbol("UNIT_A");
        var workspace = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///mod/units.xml", 5, null), null);

        var index = WithBoth([baseline], [workspace]);
        var result = index.ResolveWithShadow("UNIT_A");

        Assert.NotNull(result);
        Assert.Equal(workspace, result.Value.Winner);
        Assert.Equal(baseline, result.Value.Shadowed);
    }

    [Fact]
    public void ResolveWithShadow_Returns_Null_Shadowed_For_Baseline_Only_Symbol()
    {
        var sym = Symbol("UNIT_A");
        var index = WithBaseline(sym);
        var result = index.ResolveWithShadow("UNIT_A");

        Assert.NotNull(result);
        Assert.Equal(sym, result.Value.Winner);
        Assert.Null(result.Value.Shadowed);
    }
}
