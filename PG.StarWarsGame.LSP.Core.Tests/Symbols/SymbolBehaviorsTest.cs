// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     Behaviours as the index carries them: tokenised off an object's own tags at parse time, and
///     read back through the variant chain at query time.
/// </summary>
/// <remarks>
///     <para>
///         A behaviour answers what an object IS - a planet, a star base, a squadron - so it has to
///         be cheap enough to filter thousands of candidates by. Resolving the effective object per
///         candidate is not, which is why the tokens sit on the symbol.
///     </para>
///     <para>
///         The engine reads the merged type, so a variant that declares no behaviours of its own has
///         its base's. Measured 2026-09-21: behaviour lists carry no <c>variantMode</c> and so
///         default to replace, and half the shipped base chain plus every faction leader looks
///         behaviour-less until that walk happens.
///     </para>
/// </remarks>
public sealed class SymbolBehaviorsTest
{
    private static GameSymbol Object(string id, string? variantBase = null, params string[] behaviors)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "GameObjectType",
            new UnknownOrigin("test"), null, variantBase, behaviors.Length == 0 ? null : behaviors);
    }

    private static GameIndex IndexOf(params GameSymbol[] symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }

    // ── tokenising an object's own tags ──────────────────────────────────────

    [Fact]
    public void FromTags_UnionsTheThreeBehaviourTags()
    {
        var tokens = ObjectBehaviors.FromTags([
            ("Behavior", "PLANET, PRODUCTION"),
            ("SpaceBehavior", "SELECTABLE"),
            ("LandBehavior", "REVEAL"),
            ("Max_Health", "100")
        ]);

        Assert.Equal(["PLANET", "PRODUCTION", "SELECTABLE", "REVEAL"], tokens);
    }

    [Fact]
    public void FromTags_SplitsOnCommasAndWhitespace_AndDropsDuplicates()
    {
        var tokens = ObjectBehaviors.FromTags([
            ("Behavior", "PLANET,PRODUCTION SELECTABLE\n\tREVEAL"),
            ("SpaceBehavior", "planet")
        ]);

        Assert.Equal(["PLANET", "PRODUCTION", "SELECTABLE", "REVEAL"], tokens);
    }

    [Fact]
    public void FromTags_NoBehaviourTag_IsEmpty()
    {
        Assert.Empty(ObjectBehaviors.FromTags([("Max_Health", "100")]));
    }

    // ── reading them back through the variant chain ──────────────────────────

    [Fact]
    public void BehaviorsOf_OwnBehaviours_AnswerDirectly()
    {
        var index = IndexOf(Object("Coruscant", null, "PLANET"));

        Assert.Contains("PLANET", index.BehaviorsOf(index.Resolve("Coruscant")!));
    }

    [Fact]
    public void BehaviorsOf_IsCaseInsensitive_LikeTheEngine()
    {
        var index = IndexOf(Object("Coruscant", null, "planet"));

        Assert.Contains("PLANET", index.BehaviorsOf(index.Resolve("Coruscant")!));
    }

    // A variant that declares none inherits its base's whole list - behaviour tags default to
    // replace, so declaring none replaces nothing.
    [Fact]
    public void BehaviorsOf_VariantWithoutOwnBehaviours_AnswersWithItsBase()
    {
        var index = IndexOf(
            Object("Base_Planet", null, "PLANET", "PRODUCTION"),
            Object("Variant_Planet", "Base_Planet"));

        var behaviors = index.BehaviorsOf(index.Resolve("Variant_Planet")!);

        Assert.Equal(["PLANET", "PRODUCTION"], behaviors.OrderBy(b => b, StringComparer.Ordinal));
    }

    // Declaring its own replaces the base's rather than adding to them.
    [Fact]
    public void BehaviorsOf_VariantWithOwnBehaviours_DoesNotInherit()
    {
        var index = IndexOf(
            Object("Base_Planet", null, "PLANET"),
            Object("Variant_Marker", "Base_Planet", "MARKER"));

        var behaviors = index.BehaviorsOf(index.Resolve("Variant_Marker")!);

        Assert.Equal(["MARKER"], behaviors);
    }

    [Fact]
    public void BehaviorsOf_WalksAChainOfVariants()
    {
        var index = IndexOf(
            Object("Root", null, "PLANET"),
            Object("Middle", "Root"),
            Object("Leaf", "Middle"));

        Assert.Contains("PLANET", index.BehaviorsOf(index.Resolve("Leaf")!));
    }

    // Vanilla data is not guaranteed acyclic and a cycle here would hang the server, not fail a
    // lookup. The base may also simply not exist - a typo, or a mod that lost its dependency.
    [Fact]
    public void BehaviorsOf_CyclicChain_TerminatesEmpty()
    {
        var index = IndexOf(Object("A", "B"), Object("B", "A"));

        Assert.Empty(index.BehaviorsOf(index.Resolve("A")!));
    }

    [Fact]
    public void BehaviorsOf_UnknownBase_IsEmpty()
    {
        var index = IndexOf(Object("Orphan", "Missing_Base"));

        Assert.Empty(index.BehaviorsOf(index.Resolve("Orphan")!));
    }

    [Fact]
    public void BehaviorsOf_SymbolWithNoBehavioursAndNoBase_IsEmpty()
    {
        var index = IndexOf(Object("Plain"));

        Assert.Empty(index.BehaviorsOf(index.Resolve("Plain")!));
    }
}