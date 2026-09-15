// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The wording of the missing-bone report.
/// </summary>
/// <remarks>
///     <para>
///         <c>Collision_Mesh</c> is valid on the hull OR on the hardpoint's own
///         <c>Model_To_Attach</c>, so the diagnostic only fires once BOTH have been checked. Naming
///         only the hull made it read as though the other half had been overlooked, and the first
///         thing an author does with that message is go and look at a model the tool already
///         examined.
///     </para>
///     <para>
///         A hardpoint is <em>attached</em> to an object, never "mounted" by it - the vocabulary
///         matches the tags themselves, <c>Attachment_Bone</c> and <c>Model_To_Attach</c>.
///     </para>
/// </remarks>
public sealed class HardpointBoneNotOnModelHandlerTest
{
    private static readonly HardpointBoneNotOnModelHandler Handler = new();

    [Fact]
    public void The_owning_object_attaches_the_hardpoint_rather_than_mounting_it()
    {
        var message = Report(Fact());

        Assert.Contains("which attaches it", message, StringComparison.Ordinal);
        Assert.DoesNotContain("mount", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Both models checked, so the message has to say both - otherwise it points the author at
    ///     one model and silently drops the other.
    /// </summary>
    [Fact]
    public void Both_models_are_named_when_both_were_checked()
    {
        var message = Report(Fact(attached: "NV_pirate_turret.alo"));

        Assert.Contains("NV_pirate_frigate.alo", message, StringComparison.Ordinal);
        Assert.Contains("NV_pirate_turret.alo", message, StringComparison.Ordinal);
        Assert.Contains("Model_To_Attach", message, StringComparison.Ordinal);
        // The phrase the hardpoint E2E smoke test keys on.
        Assert.Contains("does not exist", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     With no <c>Model_To_Attach</c> there is no second model, and claiming one was checked
    ///     would be a lie of exactly the kind this change is meant to remove.
    /// </summary>
    [Fact]
    public void Only_the_hull_is_named_when_there_is_no_attached_model()
    {
        var message = Report(Fact());

        Assert.Contains("NV_pirate_frigate.alo", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Model_To_Attach", message, StringComparison.Ordinal);
    }

    /// <summary>A hardpoint checked against its own model does not talk about an owner at all.</summary>
    [Fact]
    public void A_hardpoints_own_model_is_reported_as_its_own()
    {
        var message = Report(Fact() with { OwnerId = "Pirate_Frigate_HP" });

        Assert.Contains("its own model", message, StringComparison.Ordinal);
        Assert.DoesNotContain("which attaches it", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A <c>Collision_Mesh</c> naming nothing on the model is a WARNING that says what it costs -
    ///     and what it does not.
    /// </summary>
    /// <remarks>
    ///     <c>GameObjectClass::Take_Damage</c> replaces the name it looks a hardpoint up by with the
    ///     hardpoint's own <c>Collision_Mesh</c> when the hit is aimed at that hardpoint, and on land
    ///     with the nearest live targetable hardpoint's, so aimed fire and land projectiles land. Only
    ///     untargeted space fire uses the struck renderable's name, and that is always a MESH name
    ///     (<c>alRenderableMesh::Get_Name</c>, vtable slot 0x18) - so a name on no model never comes back
    ///     from geometry. Measured, hence "never" rather than the earlier "may not".
    /// </remarks>
    [Fact]
    public void A_collision_mesh_absent_from_both_models_says_which_fire_still_reaches_it()
    {
        var result = Assert.Single(Handler.Handle(Fact("weapon.alo"), XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, result.Severity);
        Assert.Contains("Aimed fire and land projectiles still reach it", result.Message, StringComparison.Ordinal);
        Assert.Contains("untargeted space fire never does", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("may not", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no shot", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A collision mesh may name a mesh or a bone, so the report does not call it a bone.</summary>
    [Fact]
    public void A_collision_mesh_is_not_called_a_bone()
    {
        Assert.Contains("which does not exist as a mesh or a bone", Report(Fact()), StringComparison.Ordinal);
    }

    /// <summary>The other bone tags keep the wording they had: no consequence is claimed for them.</summary>
    [Fact]
    public void Other_bone_tags_keep_their_wording()
    {
        var fact = Fact() with { TagName = "Damage_Decal", BoneName = "HP_F_BLAST" };

        var message = Report(fact);

        Assert.Contains("<Damage_Decal> names bone 'HP_F_BLAST'", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Aimed fire", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The one name the model actually has, offered as the fix - the Gargantuan's
    ///     <c>HP_turret_front_00_COL</c> becomes <c>HP_turret_front_00_COLLISION</c>.
    /// </summary>
    [Fact]
    public void A_suggested_name_is_offered_as_the_fix()
    {
        var result = Assert.Single(Handler.Handle(
            Fact("weapon.alo") with { SuggestedName = "HP_F_COLLISION" }, XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal("HP_F_COLLISION", result.SuggestedFix);
        Assert.Equal("Use the model's mesh 'HP_F_COLLISION'", result.FixTitle);
    }

    [Fact]
    public void No_fix_is_offered_without_a_suggestion()
    {
        var result = Assert.Single(Handler.Handle(Fact("weapon.alo"), XmlHandlerTestFixtures.EmptyCtx));

        Assert.Null(result.SuggestedFix);
    }

    private static HardpointBoneNotOnModelFact Fact(string? attached = null)
    {
        return new HardpointBoneNotOnModelFact(
            "file:///hardpoints/Hardpoints.xml", 3, 4, 10,
            "Pirate_Frigate_HP", "Collision_Mesh", "HP_F_COLL",
            "NV_pirate_frigate.alo", "Pirate_Frigate", attached);
    }

    private static string Report(HardpointBoneNotOnModelFact fact)
    {
        return Assert.Single(Handler.Handle(fact, XmlHandlerTestFixtures.EmptyCtx)).Message;
    }
}
