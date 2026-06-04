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