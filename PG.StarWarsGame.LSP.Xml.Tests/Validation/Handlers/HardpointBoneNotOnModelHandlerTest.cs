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
