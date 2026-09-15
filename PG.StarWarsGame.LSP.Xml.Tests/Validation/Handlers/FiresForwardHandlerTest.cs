// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     What <c>Fires_Forward = Yes</c> costs, read off the effective object.
/// </summary>
/// <remarks>
///     <para>
///         Its only reader is <c>WeaponBehaviorClass::Calculate_Projectile_Facing</c>, reached only from
///         the WEAPON behaviour's <c>Fire_Projectile</c>. Set it on an object without WEAPON and it does
///         nothing - hardpoints fire through <c>HardPoint.cpp</c> and never call it. Measured over eaw/
///         and foc/: zero objects.
///     </para>
///     <para>
///         With WEAPON, it returns before <c>Is_In_Cone_Of_Fire</c>, the one firing-side reader of the
///         four <c>*Turret_*_Extent_Degrees</c> tags. Those still swing a TURRET, whose behaviour reads
///         them too, but they stop limiting the shot. Measured: one object per game,
///         <c>Y-Wing_Bombing_Run</c>, with a comment showing the author meant it - so both are hints.
///     </para>
/// </remarks>
public sealed class FiresForwardHandlerTest
{
    private static readonly FiresForwardHandler Sut = new();

    private static FiresForwardFact Fact(string id = "Y-Wing_Bombing_Run")
    {
        return new FiresForwardFact("file:///x.xml", 3, 19, 3, id);
    }

    private static DiagnosticsContext Ctx(params EffectiveTag[] tags)
    {
        return XmlHandlerTestFixtures.EmptyCtx with { Objects = new FakeObjects("Y-Wing_Bombing_Run", tags) };
    }

    private static EffectiveTag Tag(string name, string value, VariantProvenance provenance = VariantProvenance.Own)
    {
        return new EffectiveTag(name, value, string.Empty, provenance, "Y-Wing_Bombing_Run", null);
    }

    [Fact]
    public void Without_a_weapon_behaviour_it_does_nothing_and_says_so()
    {
        var d = Assert.Single(Sut.Handle(Fact(), Ctx(Tag("Behavior", "SELECTABLE"))).ToList());

        Assert.Equal(XmlDiagnosticSeverity.Hint, d.Severity);
        Assert.Equal(DiagnosticIds.FiresForwardWithoutWeapon, d.Id);
        Assert.Contains("WEAPON", d.Message);
        Assert.Contains("Y-Wing_Bombing_Run", d.Message);
    }

    [Fact]
    public void With_weapon_and_no_extents_it_is_silent()
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(Tag("LandBehavior", "SELECTABLE, WEAPON"))));
    }

    [Fact]
    public void With_weapon_the_authored_extents_are_named_as_not_limiting_the_shot()
    {
        var d = Assert.Single(Sut.Handle(Fact(), Ctx(
            Tag("LandBehavior", "WEAPON"),
            Tag("Turret_Rotate_Extent_Degrees", "20"),
            Tag("Turret_Elevate_Extent_Degrees", "20"))).ToList());

        Assert.Equal(XmlDiagnosticSeverity.Hint, d.Severity);
        Assert.Equal(DiagnosticIds.FiresForwardIgnoresArc, d.Id);
        Assert.Contains("Turret_Rotate_Extent_Degrees", d.Message);
        Assert.Contains("Turret_Elevate_Extent_Degrees", d.Message);
        Assert.DoesNotContain("swing", d.Message);
    }

    // TurretBehaviorClass reads the same tags to swing the turret bone, so they are only dead for
    // the SHOT - the message must not tell a turret author to delete them.
    [Fact]
    public void With_a_turret_the_extents_are_said_to_still_swing_it()
    {
        var d = Assert.Single(Sut.Handle(Fact(), Ctx(
            Tag("SpaceBehavior", "WEAPON, TURRET"),
            Tag("Deployed_Turret_Rotate_Extent_Degrees", "45"))).ToList());

        Assert.Contains("Deployed_Turret_Rotate_Extent_Degrees", d.Message);
        Assert.Contains("swing", d.Message);
    }

    // A variant inherits its base's behaviours; a WEAPON written on the base is as real as one here.
    [Fact]
    public void An_inherited_weapon_behaviour_counts()
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(Tag("Behavior", "WEAPON", VariantProvenance.Inherited))));
    }

    [Fact]
    public void No_object_source_or_an_unknown_object_stays_quiet()
    {
        Assert.Empty(Sut.Handle(Fact(), XmlHandlerTestFixtures.EmptyCtx));
        Assert.Empty(Sut.Handle(Fact("Somebody_Else"), Ctx(Tag("Behavior", "SELECTABLE"))));
    }

    private sealed class FakeObjects(string objectId, EffectiveTag[] tags) : IEffectiveObjectSource
    {
        public EffectiveObject Resolve(string id)
        {
            return string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase)
                ? new EffectiveObject(id, "GameObjectType", true, false, null,
                    ImmutableArray<string>.Empty, [.. tags])
                : new EffectiveObject(id, null, false, false, null,
                    ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);
        }
    }
}
