// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class GameIndexTest
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static GameSymbol Symbol(string id, string typeName = "Unit")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin("file:///f.xml", 1, null), null);
    }

    private static BaselineIndex Baseline(params GameSymbol[] symbols)
    {
        return new BaselineIndex(symbols.ToImmutableDictionary(s => s.Id),
            DateTimeOffset.UtcNow,
            "hash-abc",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);
    }

    private static GameIndex WithBaseline(params GameSymbol[] symbols)
    {
        return GameIndex.Empty with { Baseline = Baseline(symbols) };
    }

    private static GameIndex WithWorkspace(params GameSymbol[] symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols
                .GroupBy(s => s.Id)
                .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray())
        };
    }

    private static GameIndex WithBoth(GameSymbol[] baselineSymbols, GameSymbol[] workspaceSymbols)
    {
        return GameIndex.Empty with
        {
            Baseline = Baseline(baselineSymbols),
            WorkspaceDefinitions = workspaceSymbols
                .GroupBy(s => s.Id)
                .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray())
        };
    }

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

    // ── Layered precedence (project references) ───────────────────────────────

    // Builds an index where each (symbol, rank) lives in its own document carrying that LayerRank,
    // mirroring how the service stamps ranks. Symbols must have distinct origin URIs.
    private static GameIndex WithLayers(params (GameSymbol Symbol, int Rank)[] entries)
    {
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty;
        foreach (var (symbol, rank) in entries)
        {
            var uri = ((FileOrigin)symbol.Origin).Uri;
            docs = docs.SetItem(uri, new DocumentIndex(uri, 0,
                ImmutableArray.Create(symbol), ImmutableArray<GameReference>.Empty, LayerRank: rank));
        }

        var defs = entries
            .GroupBy(e => e.Symbol.Id, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(g => g.Key, g => g.Select(e => e.Symbol).ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase);

        return GameIndex.Empty with { Documents = docs, WorkspaceDefinitions = defs };
    }

    private static GameSymbol At(string id, string uri)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri, 1, null), null);
    }

    [Fact]
    public void Resolve_SameIdAcrossLayers_HighestRankWins()
    {
        var core = At("UNIT_A", "file:///core/units.xml");
        var rev = At("UNIT_A", "file:///rev/units.xml");

        // Insert the lower-rank symbol first so a naive "ws[0]" would pick the wrong one.
        var index = WithLayers((core, 0), (rev, 1));

        Assert.Equal(rev, index.Resolve("UNIT_A"));
    }

    [Fact]
    public void Resolve_SameIdAcrossLayers_OrderIndependent()
    {
        var core = At("UNIT_A", "file:///core/units.xml");
        var rev = At("UNIT_A", "file:///rev/units.xml");

        // Higher-rank symbol inserted first — winner must still be by rank, not position.
        var index = WithLayers((rev, 1), (core, 0));

        Assert.Equal(rev, index.Resolve("UNIT_A"));
    }

    [Fact]
    public void ResolveWithShadow_ReportsLowerLayerAsShadowed()
    {
        var core = At("UNIT_A", "file:///core/units.xml");
        var rev = At("UNIT_A", "file:///rev/units.xml");
        var index = WithLayers((core, 0), (rev, 1));

        var result = index.ResolveWithShadow("UNIT_A");

        Assert.NotNull(result);
        Assert.Equal(rev, result!.Value.Winner);
        Assert.Equal(core, result.Value.Shadowed);
    }

    [Fact]
    public void ResolveWithShadow_TopLayerShadowsBaseline()
    {
        var baseline = Symbol("UNIT_A");
        var rev = At("UNIT_A", "file:///rev/units.xml");
        var index = WithLayers((rev, 1)) with { Baseline = Baseline(baseline) };

        var result = index.ResolveWithShadow("UNIT_A");

        Assert.Equal(rev, result!.Value.Winner);
        Assert.Equal(baseline, result.Value.Shadowed);
    }

    [Fact]
    public void ResolveAll_OrdersByRankDescendingThenBaseline()
    {
        var core = At("UNIT_A", "file:///core/units.xml");
        var rev = At("UNIT_A", "file:///rev/units.xml");
        var baseline = Symbol("UNIT_A");
        var index = WithLayers((core, 0), (rev, 1)) with { Baseline = Baseline(baseline) };

        var all = index.ResolveAll("UNIT_A").ToList();

        Assert.Equal([rev, core, baseline], all);
    }

    [Fact]
    public void Resolve_SameRankCollision_ReturnsADefinitionAndResolveAllSeesBoth()
    {
        // Two definitions in the SAME layer (same rank) is a real duplicate, not an override.
        var a = At("UNIT_A", "file:///rev/a.xml");
        var b = At("UNIT_A", "file:///rev/b.xml");
        var index = WithLayers((a, 1), (b, 1));

        Assert.NotNull(index.Resolve("UNIT_A"));
        Assert.Equal(2, index.ResolveAll("UNIT_A").Count());
    }

    [Fact]
    public void Resolve_CaseInsensitive_Matches_Symbol_When_CaseDiffers()
    {
        // Game engine name lookups are case-insensitive.
        // "X-wing" in a reference must resolve to "X-Wing" in the definition.
        var sym = Symbol("X-Wing");
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("X-Wing", ImmutableArray.Create(sym))
        };
        Assert.Equal(sym, index.Resolve("X-wing"));
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

    // ── AllGroupMemberships ──────────────────────────────────────────────────

    [Fact]
    public void AllGroupMemberships_EmptyBaselineAndWorkspace_ReturnsEmpty()
    {
        Assert.Empty(GameIndex.Empty.AllGroupMemberships);
    }

    [Fact]
    public void AllGroupMemberships_BaselineOnlyGroups_ReturnsBaselineGroups()
    {
        var m = new GroupMembership("Unit_AT_AT", "SFXEvent", new FileOrigin("file:///sfx.xml", 0, null));
        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                GroupMemberships = ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(
                        StringComparer.OrdinalIgnoreCase)
                    .Add("Unit_AT_AT", ImmutableArray.Create(m))
            }
        };

        var all = index.AllGroupMemberships;

        Assert.True(all.ContainsKey("Unit_AT_AT"));
        Assert.Single(all["Unit_AT_AT"]);
    }

    [Fact]
    public void AllGroupMemberships_WorkspaceOnlyGroups_ReturnsWorkspaceGroups()
    {
        var m = new GroupMembership("Unit_TIE", "SFXEvent", new FileOrigin("file:///sfx.xml", 1, null));
        var index = GameIndex.Empty with
        {
            WorkspaceGroupMemberships =
            ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(StringComparer.OrdinalIgnoreCase)
                .Add("Unit_TIE", ImmutableArray.Create(m))
        };

        var all = index.AllGroupMemberships;

        Assert.True(all.ContainsKey("Unit_TIE"));
        Assert.Single(all["Unit_TIE"]);
    }

    [Fact]
    public void AllGroupMemberships_BothHaveSameKey_MergesMembers()
    {
        var baselineMember =
            new GroupMembership("Unit_AT_AT", "SFXEvent", new FileOrigin("file:///shipped.xml", 0, null));
        var workspaceMember =
            new GroupMembership("Unit_AT_AT", "SFXEvent", new FileOrigin("file:///mod.xml", 1, null));

        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                GroupMemberships = ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(
                        StringComparer.OrdinalIgnoreCase)
                    .Add("Unit_AT_AT", ImmutableArray.Create(baselineMember))
            },
            WorkspaceGroupMemberships =
            ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(StringComparer.OrdinalIgnoreCase)
                .Add("Unit_AT_AT", ImmutableArray.Create(workspaceMember))
        };

        var all = index.AllGroupMemberships;

        Assert.Equal(2, all["Unit_AT_AT"].Length);
    }

    [Fact]
    public void AllGroupMemberships_DisjointKeys_ReturnsBoth()
    {
        var bm = new GroupMembership("Baseline_Group", "SFXEvent", new FileOrigin("file:///s.xml", 0, null));
        var wm = new GroupMembership("Workspace_Group", "SFXEvent", new FileOrigin("file:///m.xml", 0, null));

        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                GroupMemberships = ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(
                        StringComparer.OrdinalIgnoreCase)
                    .Add("Baseline_Group", ImmutableArray.Create(bm))
            },
            WorkspaceGroupMemberships =
            ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(StringComparer.OrdinalIgnoreCase)
                .Add("Workspace_Group", ImmutableArray.Create(wm))
        };

        var all = index.AllGroupMemberships;

        Assert.Equal(2, all.Count);
        Assert.True(all.ContainsKey("Baseline_Group"));
        Assert.True(all.ContainsKey("Workspace_Group"));
    }
}