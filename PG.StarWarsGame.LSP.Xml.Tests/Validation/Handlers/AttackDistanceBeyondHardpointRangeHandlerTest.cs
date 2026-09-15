// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     #101: a unit that fires through hardpoints must not be told to attack from farther than they
///     reach.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the 2018 build. Movement closes to <c>Targeting_Max_Attack_Distance</c> plus the
///         target's size (<c>MovementCoordinatorClass::Compute_Targeting_Approach_Distance</c>), while
///         <c>HardPointClass::Attempt_Fire_At_Target</c> refuses a shot beyond the hardpoint's own
///         <c>Fire_Range_Distance</c> plus the target's soft radius. Set the first above the second and
///         the unit can stop where no hardpoint fires. <c>Fire_Range_Distance</c> defaults to 0.
///     </para>
///     <para>
///         A unit with the WEAPON behaviour is out of scope: there the attack distance IS the shot's
///         flight distance (<c>ProjectileFiringProperties</c>), so it cannot outrange its own weapon.
///     </para>
///     <para>
///         Corpus: the attack distance equals the longest weapon-hardpoint range on 43 foc units and
///         22 eaw units - the shipped convention - and exceeds it on 4 and 3 (<c>Tantive_IV</c> at
///         2000 against 800 among them).
///     </para>
/// </remarks>
public sealed class AttackDistanceBeyondHardpointRangeHandlerTest
{
    private static readonly AttackDistanceBeyondHardpointRangeHandler Sut = new();

    private static AttackDistanceFact Fact(bool onValue = true)
    {
        return new AttackDistanceFact("file:///x.xml", 4, 35, 6, "Tantive_IV", onValue);
    }

    private static EffectiveTag T(string owner, string name, string value)
    {
        return new EffectiveTag(name, value, string.Empty, VariantProvenance.Own, owner, null);
    }

    private static DiagnosticsContext Ctx(params (string Id, EffectiveTag[] Tags)[] objects)
    {
        return XmlHandlerTestFixtures.EmptyCtx with { Objects = new FakeObjects(objects) };
    }

    private static (string, EffectiveTag[]) Unit(string attackDistance, string hardpoints,
        string behaviours = "SELECTABLE")
    {
        var tags = new List<EffectiveTag>
            { T("Tantive_IV", "SpaceBehavior", behaviours), T("Tantive_IV", "HardPoints", hardpoints) };
        if (attackDistance.Length > 0) tags.Add(T("Tantive_IV", "Targeting_Max_Attack_Distance", attackDistance));
        return ("Tantive_IV", tags.ToArray());
    }

    private static (string, EffectiveTag[]) Hardpoint(string id, string type, string? range)
    {
        var tags = new List<EffectiveTag> { T(id, "Type", type) };
        if (range is not null) tags.Add(T(id, "Fire_Range_Distance", range));
        return (id, tags.ToArray());
    }

    [Fact]
    public void An_attack_distance_beyond_the_longest_hardpoint_range_is_reported_with_a_fix()
    {
        var d = Assert.Single(Sut.Handle(Fact(), Ctx(
            Unit("2000.0", "HP_Laser, HP_Engine"),
            Hardpoint("HP_Laser", "HARD_POINT_WEAPON_LASER", "800.0"),
            Hardpoint("HP_Engine", "HARD_POINT_ENGINE", null))).ToList());

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.AttackDistanceBeyondHardpointRange, d.Id);
        Assert.Contains("2000", d.Message);
        Assert.Contains("800", d.Message);
        Assert.Contains("HP_Laser", d.Message);
        Assert.Equal("800.0", d.SuggestedFix);
        Assert.NotNull(d.FixTitle);
    }

    [Theory]
    [InlineData("800.0")]
    [InlineData("500")]
    public void An_attack_distance_within_reach_is_accepted(string attackDistance)
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(
            Unit(attackDistance, "HP_Laser"),
            Hardpoint("HP_Laser", "HARD_POINT_WEAPON_LASER", "800.0"))));
    }

    // The longest reach counts, not the first or the shortest.
    [Fact]
    public void The_longest_weapon_hardpoint_is_the_bound()
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(
            Unit("1200", "HP_Short, HP_Long"),
            Hardpoint("HP_Short", "HARD_POINT_WEAPON_LASER", "400"),
            Hardpoint("HP_Long", "HARD_POINT_WEAPON_MISSILE", "1200"))));
    }

    [Fact]
    public void A_weapon_behaviour_takes_the_unit_out_of_scope()
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(
            Unit("2000", "HP_Laser", "SELECTABLE, WEAPON"),
            Hardpoint("HP_Laser", "HARD_POINT_WEAPON_LASER", "800"))));
    }

    // Nothing to compare against: no weapon hardpoint, or none with a positive range.
    [Fact]
    public void Without_a_ranged_weapon_hardpoint_it_is_silent()
    {
        Assert.Empty(Sut.Handle(Fact(), Ctx(
            Unit("2000", "HP_Shield, HP_Unranged"),
            Hardpoint("HP_Shield", "HARD_POINT_SHIELD_GENERATOR", "900"),
            Hardpoint("HP_Unranged", "HARD_POINT_WEAPON_LASER", null))));
    }

    // Anchored on the HardPoints list, a replacement value would overwrite the list.
    [Fact]
    public void Anchored_on_the_hardpoint_list_it_offers_no_fix()
    {
        var d = Assert.Single(Sut.Handle(Fact(false), Ctx(
            Unit("2000", "HP_Laser"),
            Hardpoint("HP_Laser", "HARD_POINT_WEAPON_LASER", "800"))).ToList());

        Assert.Null(d.SuggestedFix);
    }

    [Fact]
    public void No_object_source_or_an_unknown_unit_stays_quiet()
    {
        Assert.Empty(Sut.Handle(Fact(), XmlHandlerTestFixtures.EmptyCtx));
        Assert.Empty(Sut.Handle(Fact(), Ctx(Hardpoint("HP_Laser", "HARD_POINT_WEAPON_LASER", "800"))));
    }

    private sealed class FakeObjects((string Id, EffectiveTag[] Tags)[] objects) : IEffectiveObjectSource
    {
        public EffectiveObject Resolve(string id)
        {
            foreach (var (objectId, tags) in objects)
                if (string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase))
                    return new EffectiveObject(id, "GameObjectType", true, false, null,
                        ImmutableArray<string>.Empty, [.. tags]);

            return new EffectiveObject(id, null, false, false, null,
                ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);
        }
    }
}