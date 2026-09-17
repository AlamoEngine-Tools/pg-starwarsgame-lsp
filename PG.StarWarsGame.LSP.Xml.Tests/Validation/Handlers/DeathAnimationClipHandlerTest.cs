// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A death animation type the model has no clip for (issue #104, the model half).
/// </summary>
/// <remarks>
///     <para>
///         Measured in the 2018 build. <c>DeathBehaviorClass::Init</c> plays the type at
///         <c>Specific_Death_Anim_Index</c> (unset: a random take). <c>ModelClass::Set_Active_Animation_Type</c>
///         returns false, without an assert, when the model has no take at that index. On that failure, with
///         <c>Remove_Upon_Death</c> set and no spin-away, the object is destroyed at once; otherwise it simply
///         plays no death animation.
///     </para>
///     <para>
///         Vanilla hits 2 eaw and 10 foc objects, all without <c>Remove_Upon_Death</c>. Three are the
///         <c>Infantry_*_Melt_Death_Clone_00</c> on <c>w_infect.alo</c>, which has no clips at all and is
///         probably deliberate - reported anyway, because the engine does play nothing.
///     </para>
/// </remarks>
public sealed class DeathAnimationClipHandlerTest
{
    private static readonly DeathAnimationClipHandler Sut = new();

    private static DeathAnimationClipFact Fact(string id = "Clone")
    {
        return new DeathAnimationClipFact("file:///xml/Units.xml", 3, 30, 7, id);
    }

    private static DiagnosticsContext Ctx(string[] models, string[] files, params (string Tag, string Value)[] tags)
    {
        var bones = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models) bones[model] = ["Root"];

        var index = GameIndex.Empty with
        {
            ModelBones = bones.ToImmutable(),
            AssetFiles = new MergedAssetFileIndex(files)
        };

        return XmlHandlerTestFixtures.EmptyCtx with { Index = index, Objects = new FakeObjects("Clone", tags) };
    }

    private static readonly string[] Mandalorian = ["ni_mandalorian.alo"];

    private static readonly string[] MandalorianClips =
        ["data/art/models/ni_mandalorian_die_00.ala", "data/art/models/ni_mandalorian_move_00.ala"];

    [Fact]
    public void A_type_the_model_has_a_clip_for_is_accepted()
    {
        var ctx = Ctx(Mandalorian, MandalorianClips,
            ("Land_Model_Name", "NI_Mandalorian.alo"), ("Specific_Death_Anim_Type", "DIE"));

        Assert.Empty(Sut.Handle(Fact(), ctx));
    }

    [Fact]
    public void A_type_the_model_has_no_clip_for_is_reported()
    {
        var ctx = Ctx(Mandalorian, MandalorianClips,
            ("Land_Model_Name", "NI_Mandalorian.alo"), ("Specific_Death_Anim_Type", "Crushed"));

        var d = Assert.Single(Sut.Handle(Fact(), ctx).ToList());
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.DeathAnimationClip, d.Id);
        Assert.Contains("NI_Mandalorian.alo", d.Message);
        Assert.Contains("Crushed", d.Message);
        Assert.Contains("no death animation", d.Message);
        Assert.DoesNotContain("removed", d.Message);
    }

    // Remove_Upon_Death turns a missing animation into a unit that vanishes: Init destroys it on the failure.
    [Fact]
    public void With_Remove_Upon_Death_the_message_says_the_unit_is_removed_at_once()
    {
        var ctx = Ctx(Mandalorian, MandalorianClips,
            ("Land_Model_Name", "NI_Mandalorian.alo"), ("Specific_Death_Anim_Type", "Crushed"),
            ("Remove_Upon_Death", "Yes"));

        var d = Assert.Single(Sut.Handle(Fact(), ctx).ToList());
        Assert.Contains("removed the moment it dies", d.Message);
    }

    // Init skips the destroy while the unit spins away, so the removal is not certain.
    [Fact]
    public void Spinning_away_makes_the_removal_conditional()
    {
        var ctx = Ctx(Mandalorian, MandalorianClips,
            ("Land_Model_Name", "NI_Mandalorian.alo"), ("Specific_Death_Anim_Type", "Crushed"),
            ("Remove_Upon_Death", "true"), ("Spin_Away_On_Death", "true"));

        var d = Assert.Single(Sut.Handle(Fact(), ctx).ToList());
        Assert.Contains("unless it spins away", d.Message);
    }

    // An index past the last take fails exactly like a missing type.
    [Fact]
    public void An_index_past_the_last_take_is_reported()
    {
        var ctx = Ctx(Mandalorian, MandalorianClips,
            ("Land_Model_Name", "NI_Mandalorian.alo"), ("Specific_Death_Anim_Type", "DIE"),
            ("Specific_Death_Anim_Index", "1"));

        var d = Assert.Single(Sut.Handle(Fact(), ctx).ToList());
        Assert.Contains("Specific_Death_Anim_Index 1", d.Message);
        Assert.Contains("1 DIE take", d.Message);
    }

    [Fact]
    public void An_index_within_the_takes_is_accepted()
    {
        var ctx = Ctx(Mandalorian, MandalorianClips,
            ("Land_Model_Name", "NI_Mandalorian.alo"), ("Specific_Death_Anim_Type", "DIE"),
            ("Specific_Death_Anim_Index", "0"));

        Assert.Empty(Sut.Handle(Fact(), ctx));
    }

    // The clips come from the override model when one is named - the Swamp civilians have none of their own.
    [Fact]
    public void The_animation_override_model_supplies_the_clips()
    {
        var ctx = Ctx(["ni_gmale_swamp_a.alo", "ni_gmale_urban_a.alo"],
            ["data/art/models/ni_gmale_urban_a_fw_die_00.ala"],
            ("Land_Model_Name", "NI_Gmale_swamp_A.ALO"),
            ("Land_Model_Anim_Override_Name", "NI_GMale_Urban_A.ALO"),
            ("Specific_Death_Anim_Type", "FW_DIE"));

        Assert.Empty(Sut.Handle(Fact(), ctx));
    }

    [Fact]
    public void A_space_model_is_used_when_there_is_no_land_model()
    {
        var ctx = Ctx(["rv_ship.alo"], ["data/art/models/rv_ship_die_00.ala"],
            ("Space_Model_Name", "RV_Ship.alo"), ("Specific_Death_Anim_Type", "DIE"));

        Assert.Empty(Sut.Handle(Fact(), ctx));
    }

    // A model the index has never read cannot be judged; silence rather than a guess.
    [Fact]
    public void A_model_the_index_does_not_know_stays_quiet()
    {
        var ctx = Ctx([], [],
            ("Land_Model_Name", "NI_Unknown.alo"), ("Specific_Death_Anim_Type", "Crushed"));

        Assert.Empty(Sut.Handle(Fact(), ctx));
    }

    [Fact]
    public void An_object_with_no_model_stays_quiet()
    {
        var ctx = Ctx(Mandalorian, MandalorianClips, ("Specific_Death_Anim_Type", "Crushed"));

        Assert.Empty(Sut.Handle(Fact(), ctx));
    }

    [Fact]
    public void An_unresolved_object_stays_quiet()
    {
        var ctx = Ctx(Mandalorian, MandalorianClips,
            ("Land_Model_Name", "NI_Mandalorian.alo"), ("Specific_Death_Anim_Type", "Crushed"));

        Assert.Empty(Sut.Handle(Fact("Someone_Else"), ctx));
    }

    [Fact]
    public void No_object_source_stays_quiet()
    {
        Assert.Empty(Sut.Handle(Fact(), XmlHandlerTestFixtures.EmptyCtx));
    }

    private sealed class FakeObjects(string id, (string Tag, string Value)[] tags) : IEffectiveObjectSource
    {
        public EffectiveObject Resolve(string objectId)
        {
            if (!string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase))
                return new EffectiveObject(objectId, null, false, false, null,
                    ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

            return new EffectiveObject(id, "GameObjectType", true, false, null, [id],
                [.. tags.Select(t => new EffectiveTag(t.Tag, t.Value, t.Value, VariantProvenance.Own, id, null))]);
        }
    }
}