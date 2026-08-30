// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The damage stages an object DECLARES, which the model alone cannot reveal.
/// </summary>
/// <remarks>
///     <para>
///         The preview worked out its ALT levels by walking the loaded geometry for
///         <c>alamoAlt</c> tags. That can only ever find stages the MODEL draws something for - and
///         a stage need not: it may be nothing but an explosion and a sound declared in the XML.
///     </para>
///     <para>
///         <c>Land_Damage_Thresholds</c> / <c>Land_Damage_Alternates</c> / <c>Land_Damage_SFX</c>
///         are one positional table: at this fraction of health, show this _ALT, and play this
///         sound. 219 shipped objects declare it. The middle column is the authority on which
///         stages exist, and it disagrees with the geometry in ways that matter - <b>35</b> objects
///         declare the single alternate <c>3</c>, and <b>7</b> declare <c>1, 2, 3</c> with no stage
///         zero at all.
///     </para>
/// </remarks>
public sealed class PreviewDamageStagesTest
{
    [Fact]
    public void BuildForObject_ReadsTheDeclaredAlternates()
    {
        var scene = Build("1, 0.66, 0.33, 0", "0, 1, 2, 3");

        Assert.Equal([0, 1, 2, 3], scene.DamageStages);
    }

    // The shape 35 shipped structures use: one entry, and it is not zero. Nothing in the geometry
    // would ever suggest stage 3 exists.
    [Fact]
    public void BuildForObject_KeepsAStageThatIsNotZero()
    {
        var scene = Build("1", "3");

        Assert.Equal([3], scene.DamageStages);
    }

    [Fact]
    public void BuildForObject_KeepsAListThatSkipsStageZero()
    {
        var scene = Build("1, 0.66, 0.33", "1, 2, 3");

        Assert.Equal([1, 2, 3], scene.DamageStages);
    }

    [Fact]
    public void BuildForObject_SortsAndDeduplicatesRatherThanTrustingTheOrderWritten()
    {
        // The table is POSITIONAL, so its order belongs to the thresholds beside it. As a set of
        // which stages exist, it wants to be ascending and unique.
        var scene = Build("1, 0.5, 0", "2, 0, 2");

        Assert.Equal([0, 2], scene.DamageStages);
    }

    [Fact]
    public void BuildForObject_IgnoresEntriesThatAreNotNumbers()
    {
        var scene = Build("1, 0", "0, null");

        Assert.Equal([0], scene.DamageStages);
    }

    [Fact]
    public void BuildForObject_DeclaringNothing_LeavesTheListEmptyRatherThanGuessing()
    {
        // Every space unit. The client falls back to what the geometry tags, which is right there.
        var scene = Build(null, null);

        Assert.Empty(scene.DamageStages);
    }

    // ── the threshold table, which is what drives the stage in Gameplay ───────

    // USER RULE, 2026-09-04: the thresholds are the upper and lower BOUND of each stage.
    // `1, 0.66, 0.33, 0` against `0, 1, 2, 3` reads
    //     100% > h > 66% -> ALT0,  66% > h > 33% -> ALT1,  33% > h > 0% -> ALT2,  then ALT3.
    // So the pairing is POSITIONAL and the written order is the whole meaning of it. `DamageStages`
    // sorts and de-duplicates - correct for "which stages exist", useless for "which stage now" -
    // so the table travels separately and keeps its order.
    [Fact]
    public void BuildForObject_CarriesTheThresholdTableInWrittenOrder()
    {
        var scene = Build("1, 0.66, 0.33, 0", "0, 1, 2, 3");

        Assert.Equal([1f, 0.66f, 0.33f, 0f], scene.DamageTable.Select(row => row.Threshold));
        Assert.Equal([0, 1, 2, 3], scene.DamageTable.Select(row => row.Stage));
    }

    // Measured over foc with XML comments stripped: 219 objects declare the table, every one of them
    // has thresholds, and the two lists NEVER disagree in length. (The "42 objects" the older note
    // warns about is `Land_Damage_SFX`, which is a different column.)
    [Fact]
    public void BuildForObject_KeepsAStageThatRepeats()
    {
        // Sorting would collapse this to one entry and lose which band each belongs to.
        var scene = Build("1, 0.5, 0", "2, 2, 3");

        Assert.Equal([2, 2, 3], scene.DamageTable.Select(row => row.Stage));
    }

    [Fact]
    public void BuildForObject_LeavesTheTableEmptyWhenThresholdsAreMissing()
    {
        // The pairing is positional, so half a table is not a table. Nothing to drive the stage
        // with, and guessing an alignment would put the wrong mesh on screen at the wrong health.
        var scene = Build(null, "0, 1, 2");

        Assert.Empty(scene.DamageTable);
        Assert.Equal([0, 1, 2], scene.DamageStages);
    }

    [Fact]
    public void BuildForObject_LeavesTheTableEmptyWhenTheListsDisagree()
    {
        var scene = Build("1, 0.5", "0, 1, 2");

        Assert.Empty(scene.DamageTable);
    }

    [Fact]
    public void BuildForObject_DeclaringNothing_HasNoTable()
    {
        Assert.Empty(Build(null, null).DamageTable);
    }

    // ── a stage the model has nothing for ─────────────────────────────────────

    // The one direction worth reporting. The model is free to tag MORE than the XML asks of it -
    // that is an asset carrying more than this object uses - but a declared stage nothing draws is
    // a unit that reaches a damage state and does not change.
    //
    // Measured across foc: of the 219 objects declaring alternates, 212 have every stage tagged and
    // 6 do not - `Shuttle_Tyderium` declares 0, 1, 2 while `EV_LAMBDASHUTTLE.ALO` tags only 0 and 1.
    [Fact]
    public void BuildForObject_ReportsADeclaredStageTheModelTagsNothingFor()
    {
        var scene = Build("1, 0.66, 0.33", "0, 1, 2", ["b_root", "hull_ALT1"]);

        var problem = Assert.Single(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewDamageStageNotInModel);

        Assert.Equal("warning", problem.Severity);
        Assert.Contains("2", problem.Message);
        Assert.Contains("bunker.alo", problem.Message);
    }

    [Fact]
    public void BuildForObject_SaysNothingWhenEveryDeclaredStageIsTagged()
    {
        var scene = Build("1, 0.66, 0.33", "0, 1, 2", ["b_root", "hull_ALT1", "p_smoke_ALT2"]);

        Assert.DoesNotContain(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewDamageStageNotInModel);
    }

    // The user's rule, stated explicitly: a model supporting a stage the XML does not use is FINE.
    // The same shape as a PTE_ effect on a unit with no TURBO, or a stealth shell with no cloak.
    [Fact]
    public void BuildForObject_SaysNothingWhenTheModelTagsMoreThanIsDeclared()
    {
        var scene = Build("1, 0.66", "0, 1", ["b_root", "hull_ALT1", "hull_ALT2", "hull_ALT3"]);

        Assert.DoesNotContain(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewDamageStageNotInModel);
    }

    // Stage 0 is the undamaged state a model opens in. Nothing needs tagging for it, and every
    // untagged mesh in the file IS it - so demanding an `_ALT0` would fire on almost every object.
    [Fact]
    public void BuildForObject_NeverAsksForStageZero()
    {
        var scene = Build("1", "0", ["b_root"]);

        Assert.DoesNotContain(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewDamageStageNotInModel);
    }

    // Undecidable rather than absent: with no catalogue entry the model's tags are unknown, and a
    // warning would be about the index rather than about the file.
    [Fact]
    public void BuildForObject_SaysNothingWhenTheModelIsNotCatalogued()
    {
        var scene = Build("1, 0.66", "0, 1", null);

        Assert.DoesNotContain(scene.Problems,
            p => p.DiagnosticId == DiagnosticIds.PreviewDamageStageNotInModel);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Build(
        string? thresholds, string? alternates, IEnumerable<string>? modelNames = null)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("Bunker", [new GameSymbol("Bunker", GameSymbolKind.XmlObject, "GroundStructure",
                    new FileOrigin("file:///structures.xml", 0, 0), null, null)]),

            // Bones UNION mesh names, which is what the catalogue holds. Measured across 1957
            // shipped models: every level an ALT-tagged proxy declares is also tagged by a bone or a
            // mesh, so this sees the whole of what a model stages.
            ModelBones = modelNames is null
                ? ImmutableDictionary<string, ImmutableArray<string>>.Empty
                : ImmutableDictionary<string, ImmutableArray<string>>.Empty
                    .Add(ModelBoneKey.From("bunker.alo"), [.. modelNames])
        };

        // ONE call. `With` REPLACES an object's tag list rather than adding to it, so the chain this
        // used to be kept only the last tag - every test here was quietly running against an object
        // with no model at all, which is why none of them ever saw a hull.
        var declared = new List<VariantTag> { Tag("Land_Model_Name", "bunker.alo") };
        if (thresholds is not null)
            declared.Add(Tag("Land_Damage_Thresholds", thresholds));
        if (alternates is not null)
            declared.Add(Tag("Land_Damage_Alternates", alternates));

        var tags = new FakeVariantTagSource().With("Bunker", [.. declared]);

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets("bunker.alo")).BuildForObject("Bunker");
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }
}
