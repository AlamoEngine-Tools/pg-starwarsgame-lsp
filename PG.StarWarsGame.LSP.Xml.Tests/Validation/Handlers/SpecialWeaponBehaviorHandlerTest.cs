// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A faction's special weapon must actually be a special weapon (issue #98).
/// </summary>
/// <remarks>
///     <para>
///         The issue asked to enforce a name whitelist - "Hypervelocity Cannon or Ion Cannon" - and the
///         reporter withdrew that in the comments once they noticed EaWX uses a <c>Ground_</c> prefix.
///         Vanilla settles it: the shipped values are <c>Ground_Ion_Cannon</c> and
///         <c>Ground_Empire_Hypervelocity_Gun</c>, so a name check would fire on the base game.
///     </para>
///     <para>
///         The reporter's revised rule was the behaviour, and the engine agrees with a correction:
///         <c>GameModeClass::Add_Special_Weapon</c> registers the weapon only if it behaves like
///         <c>SPECIAL_WEAPON</c> OR <c>LOBBING_SUPERWEAPON</c>, and otherwise returns false without an
///         assert - the weapon is never registered, so there is nothing to fire.
///     </para>
/// </remarks>
public sealed class SpecialWeaponBehaviorHandlerTest
{
    private static readonly SpecialWeaponBehaviorHandler Sut = new();

    private static XmlTagDefinition WeaponTag()
    {
        return new XmlTagDefinition
        {
            Tag = "Standalone_Space_Maps_Special_Weapon_A",
            ValueType = XmlValueType.TypeReferenceList,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceTypeName = "GameObjectType"
        };
    }

    private static DiagnosticsContext CtxWith(params (string Id, string BehaviorTag, string Value)[] objects)
    {
        return XmlHandlerTestFixtures.EmptyCtx with { Objects = new FakeObjects(objects) };
    }

    [Fact]
    public void Object_with_SPECIAL_WEAPON_is_accepted()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "Ground_Ion_Cannon");
        var ctx = CtxWith(("Ground_Ion_Cannon", "SpaceBehavior", "SPECIAL_WEAPON"));

        Assert.Empty(Sut.Handle(fact, ctx));
    }

    // The behaviour may sit in any of the three tags - reading only SpaceBehavior would reject a
    // perfectly good weapon authored the other way.
    [Theory]
    [InlineData("Behavior")]
    [InlineData("LandBehavior")]
    public void Behaviour_counts_from_any_tag(string behaviorTag)
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "W");
        var ctx = CtxWith(("W", behaviorTag, "SPECIAL_WEAPON"));

        Assert.Empty(Sut.Handle(fact, ctx));
    }

    // Add_Special_Weapon accepts either behaviour. Rejecting a lobbing superweapon reported a weapon
    // the engine registers.
    [Fact]
    public void Object_with_LOBBING_SUPERWEAPON_is_accepted()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "Ground_Magnepulse_Cannon");
        var ctx = CtxWith(("Ground_Magnepulse_Cannon", "SpaceBehavior", "TURRET, LOBBING_SUPERWEAPON"));

        Assert.Empty(Sut.Handle(fact, ctx));
    }

    [Fact]
    public void Object_without_the_behaviour_is_reported()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "Ground_Barracks");
        var ctx = CtxWith(("Ground_Barracks", "LandBehavior", "SELECTABLE, REVEAL"));

        var d = Assert.Single(Sut.Handle(fact, ctx).ToList());
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Ground_Barracks", d.Message);
        Assert.Contains("SPECIAL_WEAPON", d.Message);
        Assert.Contains("LOBBING_SUPERWEAPON", d.Message);
        // What the engine does, not a guess about firing.
        Assert.Contains("never registers it", d.Message);
    }

    // B has no command bar button and is deprecated; the deprecation is the one warning it gets.
    [Fact]
    public void Weapon_B_is_left_to_the_deprecation()
    {
        var b = new XmlTagDefinition
        {
            Tag = "Standalone_Space_Maps_Special_Weapon_B",
            ValueType = XmlValueType.TypeReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceTypeName = "GameObjectType"
        };
        var fact = XmlHandlerTestFixtures.MakeFact(b, "Ground_Barracks");
        var ctx = CtxWith(("Ground_Barracks", "LandBehavior", "SELECTABLE"));

        Assert.Empty(Sut.Handle(fact, ctx));
    }

    // An id nothing defines is the unresolved-reference check's business. Reporting it here too
    // would put a second, more confusing diagnostic on one typo.
    [Fact]
    public void Unknown_object_is_left_to_the_reference_check()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "Does_Not_Exist");
        var ctx = CtxWith(("Something_Else", "SpaceBehavior", "SPECIAL_WEAPON"));

        Assert.Empty(Sut.Handle(fact, ctx));
    }

    // Without a resolver the question cannot be answered, and silence is the only honest answer -
    // treating "cannot tell" as "fails" would light up every faction in a narrow context.
    [Fact]
    public void No_object_source_stays_quiet()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "Ground_Ion_Cannon");

        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Other_tags_are_ignored()
    {
        var other = new XmlTagDefinition
        {
            Tag = "Land_Bomber_Unit",
            ValueType = XmlValueType.TypeReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceTypeName = "GameObjectType"
        };
        var fact = XmlHandlerTestFixtures.MakeFact(other, "Ground_Barracks");
        var ctx = CtxWith(("Ground_Barracks", "LandBehavior", "SELECTABLE"));

        Assert.Empty(Sut.Handle(fact, ctx));
    }

    private sealed class FakeObjects(params (string Id, string BehaviorTag, string Value)[] objects)
        : IEffectiveObjectSource
    {
        public EffectiveObject Resolve(string objectId)
        {
            var match = objects.FirstOrDefault(o =>
                string.Equals(o.Id, objectId, StringComparison.OrdinalIgnoreCase));

            if (match.Id is null)
                return new EffectiveObject(objectId, null, false, false, null,
                    ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

            return new EffectiveObject(
                match.Id, "GameObjectType", true, false, null,
                ImmutableArray<string>.Empty,
                [
                    new EffectiveTag(match.BehaviorTag, match.Value, match.Value,
                        VariantProvenance.Own, match.Id, null)
                ]);
        }
    }
}