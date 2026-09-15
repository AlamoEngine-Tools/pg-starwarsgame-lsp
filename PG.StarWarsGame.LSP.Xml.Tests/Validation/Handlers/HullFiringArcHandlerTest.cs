// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A5: on a WEAPON unit with no TURRET, the turret-extent tags are the hull's firing arc.
/// </summary>
/// <remarks>
///     <para>
///         <c>WeaponBehaviorClass::Is_In_Cone_Of_Fire</c> refuses a shot whose yaw exceeds
///         <c>Turret_Rotate_Extent_Degrees</c> or whose pitch exceeds <c>Turret_Elevate_Extent_Degrees</c>
///         (the deployed pair while deployed; no pitch test with <c>Turret_XY_Only</c>), measured from the
///         OBJECT's facing. The only other reader is <c>TurretBehaviorClass</c>. So without TURRET the tags
///         read as turret configuration and act as the hull's arc.
///     </para>
///     <para>
///         Not a fault: 86 foc and 41 eaw objects do it, nearly all fighters and speeders whose guns point
///         forward on purpose. A hint that says what the numbers do, nothing more. Yaw under 180 and pitch
///         under 90 are the only values that restrict anything - a bearing cannot exceed 180 nor a pitch 90.
///     </para>
/// </remarks>
public sealed class HullFiringArcHandlerTest
{
    private static readonly HullFiringArcHandler Sut = new();

    private static HullFiringArcFact Fact()
    {
        return new HullFiringArcFact("file:///x.xml", 3, 34, 4, "X-Wing");
    }

    private static EffectiveTag T(string name, string value)
    {
        return new EffectiveTag(name, value, string.Empty, VariantProvenance.Own, "X-Wing", null);
    }

    private static DiagnosticsContext Ctx(params EffectiveTag[] tags)
    {
        return XmlHandlerTestFixtures.EmptyCtx with { Objects = new FakeObjects(tags) };
    }

    [Fact]
    public void A_fighter_with_a_forward_arc_gets_a_hint_naming_both_bounds()
    {
        var d = Assert.Single(Sut.Handle(Fact(), Ctx(
            T("SpaceBehavior", "SELECTABLE, WEAPON"),
            T("Turret_Rotate_Extent_Degrees", "20.0"),
            T("Turret_Elevate_Extent_Degrees", "40.0"))).ToList());

        Assert.Equal(XmlDiagnosticSeverity.Hint, d.Severity);
        Assert.Equal(DiagnosticIds.HullFiringArc, d.Id);
        Assert.Contains("X-Wing", d.Message);
        Assert.Contains("Turret_Rotate_Extent_Degrees", d.Message);
        Assert.Contains("20", d.Message);
        Assert.Contains("Turret_Elevate_Extent_Degrees", d.Message);
        Assert.Contains("40", d.Message);
    }

    [Fact]
    public void A_turret_behaviour_means_the_tags_are_turret_configuration()
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(
            T("LandBehavior", "WEAPON, TURRET"),
            T("Turret_Rotate_Extent_Degrees", "20"))));
    }

    [Fact]
    public void Without_a_weapon_behaviour_nothing_reads_the_arc()
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(
            T("SpaceBehavior", "SELECTABLE"),
            T("Turret_Rotate_Extent_Degrees", "20"))));
    }

    // Fires_Forward skips the arc test entirely; FiresForwardHandler already says so.
    [Fact]
    public void Fires_forward_is_left_to_its_own_hint()
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(
            T("SpaceBehavior", "WEAPON"),
            T("Fires_Forward", "Yes"),
            T("Turret_Rotate_Extent_Degrees", "20"))));
    }

    // 180 either side is the whole circle and 90 up or down the whole sphere: nothing is restricted.
    [Fact]
    public void Unrestrictive_values_are_silent()
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(
            T("SpaceBehavior", "WEAPON"),
            T("Turret_Rotate_Extent_Degrees", "180"),
            T("Turret_Elevate_Extent_Degrees", "90"))));
    }

    // Turret_XY_Only drops the pitch test, so a tight elevate bound restricts nothing.
    [Fact]
    public void Xy_only_leaves_only_the_rotate_bound()
    {
        var d = Assert.Single(Sut.Handle(Fact(), Ctx(
            T("SpaceBehavior", "WEAPON"),
            T("Turret_XY_Only", "Yes"),
            T("Turret_Rotate_Extent_Degrees", "30"),
            T("Turret_Elevate_Extent_Degrees", "10"))).ToList());

        Assert.Contains("Turret_Rotate_Extent_Degrees", d.Message);
        Assert.DoesNotContain("Turret_Elevate_Extent_Degrees", d.Message);

        Assert.Empty(Sut.Handle(Fact(), Ctx(
            T("SpaceBehavior", "WEAPON"),
            T("Turret_XY_Only", "Yes"),
            T("Turret_Elevate_Extent_Degrees", "10"))));
    }

    [Fact]
    public void The_deployed_pair_counts_too()
    {
        var d = Assert.Single(Sut.Handle(Fact(), Ctx(
            T("LandBehavior", "WEAPON"),
            T("Deployed_Turret_Rotate_Extent_Degrees", "45"))).ToList());

        Assert.Contains("Deployed_Turret_Rotate_Extent_Degrees", d.Message);
    }

    [Fact]
    public void No_object_source_or_an_unknown_object_stays_quiet()
    {
        Assert.Empty(Sut.Handle(Fact(), XmlHandlerTestFixtures.EmptyCtx));
        Assert.Empty(Sut.Handle(new HullFiringArcFact("file:///x.xml", 1, 1, 1, "Somebody_Else"),
            Ctx(T("SpaceBehavior", "WEAPON"), T("Turret_Rotate_Extent_Degrees", "20"))));
    }

    private sealed class FakeObjects(EffectiveTag[] tags) : IEffectiveObjectSource
    {
        public EffectiveObject Resolve(string id)
        {
            return string.Equals(id, "X-Wing", StringComparison.OrdinalIgnoreCase)
                ? new EffectiveObject(id, "GameObjectType", true, false, null, ImmutableArray<string>.Empty, [.. tags])
                : new EffectiveObject(id, null, false, false, null, ImmutableArray<string>.Empty,
                    ImmutableArray<EffectiveTag>.Empty);
        }
    }
}