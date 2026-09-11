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
///         <c>EJECT_VEHICLE_THIEF</c> ability, and must have <c>GARRISON_VEHICLE</c> removed. Only
///         the first is enforced, and the corpus is why. All 18 clones vanilla points at carry
///         EJECT_VEHICLE_THIEF, so that rule fires on nothing shipped and is mechanically necessary
///         anyway - without it the thief can never get out, which is the clone's whole purpose.
///     </para>
///     <para>
///         The GARRISON_VEHICLE half is deliberately NOT enforced. Two of those 18 -
///         F9TZ_Cloaking_Transport_Captured and HAV_Juggernaut_Captured - declare no behaviour tag
///         of their own and so inherit GARRISON_VEHICLE from their base, meaning the effective
///         object really does keep it. The GlyphX release covers the Lua garrison wrapper but not
///         the capture mechanic, so nothing available says whether that actually breaks anything.
///         An unverified rule that fires on shipped data is the mistake #98 was.
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

    private static DiagnosticsContext Ctx(string objectId, string abilitiesFragment)
    {
        return XmlHandlerTestFixtures.EmptyCtx with
        {
            Objects = new FakeObjects(objectId, abilitiesFragment)
        };
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

    private sealed class FakeObjects(string objectId, string abilitiesFragment) : IEffectiveObjectSource
    {
        public EffectiveObject Resolve(string id)
        {
            if (!string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase))
                return new EffectiveObject(id, null, false, false, null,
                    ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

            var tags = abilitiesFragment.Length == 0
                ? ImmutableArray<EffectiveTag>.Empty
                : ImmutableArray.Create(new EffectiveTag(
                    "Unit_Abilities_Data", string.Empty, abilitiesFragment,
                    VariantProvenance.Own, id, null));

            return new EffectiveObject(id, "GameObjectType", true, false, null,
                ImmutableArray<string>.Empty, tags);
        }
    }
}
