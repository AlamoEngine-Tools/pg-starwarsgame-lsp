// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A capture clone must be able to let the thief back out (issue #103).
/// </summary>
/// <remarks>
///     <para>
///         The tag description states two conditions: the object must have the
///         <c>EJECT_VEHICLE_THIEF</c> ability, and must have <c>GARRISON_VEHICLE</c> removed. Both are
///         enforced, under separate ids so either can be silenced alone.
///     </para>
///     <para>
///         The ability half is an engine assert: <c>VehicleThiefBehaviorClass::Begin_Stealing_Vehicle</c>
///         asserts the clone has EJECT_VEHICLE_THIEF and only activates the eject when it does. All 18
///         clones vanilla points at carry it.
///     </para>
///     <para>
///         The behaviour half was held back until the maintainer asked for it. The capture never reads
///         GARRISON_VEHICLE, but it puts the thief into the vehicle's flagship container, and
///         <c>GarrisonableBehaviorClass::Garrison_Unit</c> loads garrisoned units into that same
///         container. Two vanilla clones - F9TZ_Cloaking_Transport_Captured and
///         HAV_Juggernaut_Captured - inherit the behaviour from their base and are reported; a vanilla
///         hit is not a veto.
///     </para>
/// </remarks>
public sealed class VehicleThiefCloneHandlerTest
{
    private static readonly VehicleThiefCloneHandler Sut = new();

    private const string Abilities =
        "<Unit_Abilities_Data SubObjectList=\"Yes\">" +
        "<Unit_Ability><Type>HUNT</Type></Unit_Ability>" +
        "<Unit_Ability><Type>EJECT_VEHICLE_THIEF</Type><Recharge_Seconds>10</Recharge_Seconds></Unit_Ability>" +
        "</Unit_Abilities_Data>";

    private const string NoEject =
        "<Unit_Abilities_Data SubObjectList=\"Yes\">" +
        "<Unit_Ability><Type>HUNT</Type></Unit_Ability>" +
        "</Unit_Abilities_Data>";

    private static XmlTagDefinition CloneTag()
    {
        return new XmlTagDefinition
        {
            Tag = "Vehicle_Thief_Inside_Clone",
            ValueType = XmlValueType.TypeReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceTypeName = "GameObjectType"
        };
    }

    private static DiagnosticsContext Ctx(string objectId, string abilitiesFragment,
        params (string Tag, string Value)[] behaviours)
    {
        return XmlHandlerTestFixtures.EmptyCtx with
        {
            Objects = new FakeObjects(objectId, abilitiesFragment, behaviours)
        };
    }

    [Fact]
    public void Clone_that_keeps_garrison_vehicle_is_reported_under_its_own_id()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "HAV_Juggernaut_Captured");

        var d = Assert.Single(Sut.Handle(fact,
            Ctx("HAV_Juggernaut_Captured", Abilities, ("LandBehavior", "SELECTABLE, GARRISON_VEHICLE"))).ToList());

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.VehicleThiefCloneGarrison, d.Id);
        Assert.Contains("HAV_Juggernaut_Captured", d.Message);
        Assert.Contains("GARRISON_VEHICLE", d.Message);
    }

    // Behaviours live in any of three tags; the plain Behavior tag counts as much as LandBehavior.
    [Fact]
    public void Garrison_vehicle_in_the_plain_behavior_tag_counts()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "Clone");

        Assert.Single(Sut.Handle(fact, Ctx("Clone", Abilities, ("Behavior", "GARRISON_VEHICLE"))).ToList());
    }

    [Fact]
    public void Clone_with_other_behaviours_only_is_accepted()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "Clone");

        Assert.Empty(Sut.Handle(fact, Ctx("Clone", Abilities, ("LandBehavior", "SELECTABLE, GARRISON_UNIT"))));
    }

    [Fact]
    public void A_clone_breaking_both_conditions_gets_both_reports()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "Clone");

        var ids = Sut.Handle(fact, Ctx("Clone", NoEject, ("LandBehavior", "GARRISON_VEHICLE")))
            .Select(d => d.Id).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(DiagnosticIds.VehicleThiefCloneGarrison, ids);
    }

    [Fact]
    public void Clone_with_the_eject_ability_is_accepted()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "Pod_Walker_Captured");

        Assert.Empty(Sut.Handle(fact, Ctx("Pod_Walker_Captured", Abilities)));
    }

    [Fact]
    public void Clone_without_it_is_reported()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "Pod_Walker_Captured");

        var d = Assert.Single(Sut.Handle(fact, Ctx("Pod_Walker_Captured", NoEject)).ToList());
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Pod_Walker_Captured", d.Message);
        Assert.Contains("EJECT_VEHICLE_THIEF", d.Message);
    }

    [Fact]
    public void Clone_with_no_abilities_at_all_is_reported()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "Bare");

        Assert.Single(Sut.Handle(fact, Ctx("Bare", string.Empty)).ToList());
    }

    // The ability type is read from the ability's Type element, not by looking for the word
    // anywhere in the fragment - a recharge comment or another ability's parameter must not pass.
    [Fact]
    public void The_name_must_be_an_ability_Type_not_just_present_in_the_text()
    {
        const string decoy =
            "<Unit_Abilities_Data SubObjectList=\"Yes\">" +
            "<Unit_Ability><Type>HUNT</Type>" +
            "<Blocked_By>EJECT_VEHICLE_THIEF</Blocked_By></Unit_Ability>" +
            "</Unit_Abilities_Data>";
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "Decoy");

        Assert.Single(Sut.Handle(fact, Ctx("Decoy", decoy)).ToList());
    }

    [Fact]
    public void Unknown_object_is_left_to_the_reference_check()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "Does_Not_Exist");

        Assert.Empty(Sut.Handle(fact, Ctx("Something_Else", Abilities)));
    }

    [Fact]
    public void No_object_source_stays_quiet()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(CloneTag(), "Pod_Walker_Captured");

        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Other_tags_are_ignored()
    {
        var other = new XmlTagDefinition
        {
            Tag = "Obstacle_Proxy_Type",
            ValueType = XmlValueType.TypeReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceTypeName = "GameObjectType"
        };
        var fact = XmlHandlerTestFixtures.MakeFact(other, "Bare");

        Assert.Empty(Sut.Handle(fact, Ctx("Bare", string.Empty)));
    }

    private sealed class FakeObjects(string objectId, string abilitiesFragment,
        (string Tag, string Value)[] behaviours) : IEffectiveObjectSource
    {
        public EffectiveObject Resolve(string id)
        {
            if (!string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase))
                return new EffectiveObject(id, null, false, false, null,
                    ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

            var tags = ImmutableArray.CreateBuilder<EffectiveTag>();
            if (abilitiesFragment.Length > 0)
                tags.Add(new EffectiveTag("Unit_Abilities_Data", string.Empty, abilitiesFragment,
                    VariantProvenance.Own, id, null));
            foreach (var (tag, value) in behaviours)
                tags.Add(new EffectiveTag(tag, value, string.Empty, VariantProvenance.Own, id, null));

            return new EffectiveObject(id, "GameObjectType", true, false, null,
                ImmutableArray<string>.Empty, tags.ToImmutable());
        }
    }
}
