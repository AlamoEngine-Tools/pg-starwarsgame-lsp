// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     Two symbols parsed from the same unchanged text must be equal.
/// </summary>
/// <remarks>
///     <para>
///         This is not a nicety. <c>GameIndexService.ApplyDocumentIndex</c> compares the old and new
///         symbol lists with <c>SequenceEqual</c> to recognise a content-only re-parse, and on a
///         match it keeps the workspace dictionaries reference-identical - which is what lets the
///         diagnostics publisher re-publish only the edited document.
///     </para>
///     <para>
///         A record compares array members by REFERENCE, so the moment a symbol carried its
///         behaviours and flags as arrays every re-parse produced "different" symbols, that guard
///         stopped firing, and every edit rebuilt the whole index and re-published beyond the file
///         the author touched. It surfaced as an end-to-end rename leaving stale diagnostics behind.
///     </para>
/// </remarks>
public sealed class GameSymbolEqualityTest
{
    private static GameSymbol Symbol(string[]? behaviors, string[]? flags)
    {
        return new GameSymbol("Alderaan", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin("file:///planets.xml", 3, 7), null, null, behaviors, flags);
    }

    [Fact]
    public void Equals_SameBehaviours_InDifferentArrays_AreEqual()
    {
        var first = Symbol(["PLANET", "PRODUCTION"], null);
        var second = Symbol(["PLANET", "PRODUCTION"], null);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_SameFlags_InDifferentArrays_AreEqual()
    {
        var first = Symbol(null, ["Is_Named_Hero"]);
        var second = Symbol(null, ["Is_Named_Hero"]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    // The common case after chunk 3: every inspected object carries an empty flag list.
    [Fact]
    public void Equals_TwoEmptyFlagLists_AreEqual()
    {
        Assert.Equal(Symbol(null, []), Symbol(null, []));
    }

    // "Inspected and has none" is a different symbol from "nobody looked", and the kind matcher
    // reads them differently, so equality has to keep them apart.
    [Fact]
    public void Equals_EmptyIsNotTheSameAsNull()
    {
        Assert.NotEqual(Symbol(null, []), Symbol(null, null));
        Assert.NotEqual(Symbol([], null), Symbol(null, null));
    }

    [Fact]
    public void Equals_DifferentBehaviours_AreNotEqual()
    {
        Assert.NotEqual(Symbol(["PLANET"], null), Symbol(["DUMMY_STARSHIP"], null));
    }

    // Order is how the object wrote them; a different order is a different edit.
    [Fact]
    public void Equals_SameBehavioursInADifferentOrder_AreNotEqual()
    {
        Assert.NotEqual(Symbol(["PLANET", "PRODUCTION"], null), Symbol(["PRODUCTION", "PLANET"], null));
    }

    [Fact]
    public void Equals_EverythingElseStillCounts()
    {
        var baseline = Symbol(["PLANET"], []);

        Assert.NotEqual(baseline, baseline with { Id = "Hoth" });
        Assert.NotEqual(baseline, baseline with { TypeName = "Faction" });
        Assert.NotEqual(baseline, baseline with { VariantBaseId = "Base_Planet" });
        Assert.NotEqual(baseline, baseline with { Description = "a planet" });
        Assert.NotEqual(baseline, baseline with { Origin = new UnknownOrigin("engine") });
    }
}
