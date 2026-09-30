// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     Deciding whether an object is of a kind, for the two callers that need it: completion, which
///     proposes what matches, and validation, which reports what does not.
/// </summary>
/// <remarks>
///     The third answer is the important one. A predicate can rest on something the index does not
///     carry - membership is never captured, and a baseline built before flags has none - and
///     guessing there costs either a hidden proposal or, far worse, an error on correct data. So it
///     says it does not know, completion proposes anyway, and validation stays quiet.
/// </remarks>
public sealed class ObjectKindsTest
{
    private static ObjectKindDefinition Kind(string name, string[]? behaviors = null,
        string[]? flags = null, string[]? memberOf = null)
    {
        return new ObjectKindDefinition
        {
            Kind = name, Behaviors = behaviors ?? [], Flags = flags ?? [], MemberOf = memberOf ?? []
        };
    }

    private static GameSymbol Object(string id, string[]? behaviors = null, string[]? flags = null,
        string? variantBase = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "GameObjectType", new UnknownOrigin("test"),
            null, variantBase, behaviors, flags);
    }

    private static GameIndex IndexOf(params GameSymbol[] symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }

    // ── behaviour predicates ────────────────────────────────────────────────

    [Fact]
    public void Match_BehaviourPresent_IsYes()
    {
        var index = IndexOf(Object("Coruscant", ["PLANET", "PRODUCTION"]));

        Assert.Equal(KindMatch.Yes,
            ObjectKinds.Match(index, index.Resolve("Coruscant")!, Kind("Planet", ["PLANET"])));
    }

    [Fact]
    public void Match_BehaviourAbsent_IsNo()
    {
        var index = IndexOf(Object("X_Wing", ["DUMMY_STARSHIP"]));

        Assert.Equal(KindMatch.No,
            ObjectKinds.Match(index, index.Resolve("X_Wing")!, Kind("Planet", ["PLANET"])));
    }

    // Any token within a group is enough - a base is a ground base OR a star base.
    [Fact]
    public void Match_AnyBehaviourInTheGroup_IsEnough()
    {
        var index = IndexOf(Object("Empire_Star_Base_1", ["DUMMY_STAR_BASE"]));

        Assert.Equal(KindMatch.Yes, ObjectKinds.Match(index, index.Resolve("Empire_Star_Base_1")!,
            Kind("Base", ["DUMMY_GROUND_BASE", "DUMMY_STAR_BASE"])));
    }

    // Behaviours come through the variant chain, so a variant of a planet is a planet.
    [Fact]
    public void Match_BehaviourInheritedFromVariantBase_IsYes()
    {
        var index = IndexOf(
            Object("Base_Planet", ["PLANET"]),
            Object("Variant_Planet", variantBase: "Base_Planet"));

        Assert.Equal(KindMatch.Yes,
            ObjectKinds.Match(index, index.Resolve("Variant_Planet")!, Kind("Planet", ["PLANET"])));
    }

    // ── flag predicates ─────────────────────────────────────────────────────

    [Fact]
    public void Match_FlagSet_IsYes()
    {
        var index = IndexOf(Object("Vader", ["DUMMY_STARSHIP"], flags: ["Is_Named_Hero"]));

        Assert.Equal(KindMatch.Yes, ObjectKinds.Match(index, index.Resolve("Vader")!,
            Kind("HeroUnit", flags: ["Is_Named_Hero", "Is_Generic_Hero"])));
    }

    // An object that WAS inspected and had none is a definite no - an empty list, not a missing one.
    [Fact]
    public void Match_InspectedAndNoFlagSet_IsNo()
    {
        var index = IndexOf(Object("Crate", ["LAND_OBSTACLE"], flags: []));

        Assert.Equal(KindMatch.No,
            ObjectKinds.Match(index, index.Resolve("Crate")!, Kind("HeroUnit", flags: ["Is_Named_Hero"])));
    }

    // A symbol from a baseline built before flags carries none at all. Saying "not a hero" there
    // would put an error on every shipped hero, so it says it does not know instead.
    [Fact]
    public void Match_FlagsNeverCaptured_IsUnknown()
    {
        var index = IndexOf(Object("Vader", ["DUMMY_STARSHIP"]));

        Assert.Equal(KindMatch.Unknown,
            ObjectKinds.Match(index, index.Resolve("Vader")!, Kind("HeroUnit", flags: ["Is_Named_Hero"])));
    }

    // ── membership, which nothing captures ──────────────────────────────────

    [Fact]
    public void Match_MembershipPredicate_IsUnknown()
    {
        var index = IndexOf(Object("Rebel_Pilot", ["IDLE"], flags: []));

        Assert.Equal(KindMatch.Unknown, ObjectKinds.Match(index, index.Resolve("Rebel_Pilot")!,
            Kind("SquadronUnit", memberOf: ["Squadron_Units"])));
    }

    // ── groups combine ──────────────────────────────────────────────────────

    // Every group has to hold: a hero company is a ground company AND carries a hero flag.
    [Fact]
    public void Match_AllGroupsMustHold()
    {
        var index = IndexOf(
            Object("Vader_Team", ["DUMMY_GROUND_COMPANY"], flags: ["Is_Named_Hero"]),
            Object("Plain_Team", ["DUMMY_GROUND_COMPANY"], flags: []));
        var heroCompany = Kind("HeroCompany", ["DUMMY_GROUND_COMPANY"], ["Is_Named_Hero"]);

        Assert.Equal(KindMatch.Yes, ObjectKinds.Match(index, index.Resolve("Vader_Team")!, heroCompany));
        Assert.Equal(KindMatch.No, ObjectKinds.Match(index, index.Resolve("Plain_Team")!, heroCompany));
    }

    // A group that definitely fails settles it, even when another cannot be judged - the object is
    // not of the kind whatever the unknown turns out to be.
    [Fact]
    public void Match_ADefiniteNoBeatsAnUnknown()
    {
        var index = IndexOf(Object("X_Wing", ["DUMMY_STARSHIP"]));

        Assert.Equal(KindMatch.No, ObjectKinds.Match(index, index.Resolve("X_Wing")!,
            Kind("HeroCompany", ["DUMMY_GROUND_COMPANY"], ["Is_Named_Hero"])));
    }

    [Fact]
    public void Match_KindWithNoPredicate_IsUnknown()
    {
        var index = IndexOf(Object("Anything", ["IDLE"]));

        Assert.Equal(KindMatch.Unknown, ObjectKinds.Match(index, index.Resolve("Anything")!, Kind("Empty")));
    }
}
