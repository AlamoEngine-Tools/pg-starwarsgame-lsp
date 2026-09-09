// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     Reading an object's behaviour list, which is the question most cross-object diagnostics
///     actually ask.
/// </summary>
/// <remarks>
///     The trap this exists to close: behaviours are spread across THREE tags. Measured over the
///     shipped corpus by the preview work, <c>SpaceBehavior</c> carries them 98 times, plain
///     <c>Behavior</c> 32 and <c>LandBehavior</c> 19 - so a check that reads only one tag reports
///     a missing behaviour on an object that plainly has it.
/// </remarks>
public sealed class ObjectBehaviorsTest
{
    private static EffectiveObject WithTags(params (string Tag, string Value)[] tags)
    {
        return new EffectiveObject(
            "Obj", "GameObjectType", true, false, null,
            ImmutableArray<string>.Empty,
            tags.Select(t => new EffectiveTag(
                    t.Tag, t.Value, t.Value, VariantProvenance.Own, "Obj", null))
                .ToImmutableArray());
    }

    [Theory]
    [InlineData("Behavior")]
    [InlineData("SpaceBehavior")]
    [InlineData("LandBehavior")]
    public void Reads_every_tag_that_carries_behaviours(string tag)
    {
        var behaviours = ObjectBehaviors.Of(WithTags((tag, "SPECIAL_WEAPON")));

        Assert.Contains("SPECIAL_WEAPON", behaviours);
    }

    // Vanilla writes them with commas, spaces, or both, and often ragged.
    [Fact]
    public void Splits_on_commas_and_whitespace_alike()
    {
        var behaviours = ObjectBehaviors.Of(WithTags(
            ("LandBehavior", "SPACE_OBSTACLE, LAND_OBSTACLE,SELECTABLE\tREVEAL")));

        Assert.Equal(
            ["LAND_OBSTACLE", "REVEAL", "SELECTABLE", "SPACE_OBSTACLE"],
            behaviours.OrderBy(b => b, StringComparer.Ordinal));
    }

    // An object with both a space and a land list has the union - which is why "does it have X"
    // cannot be answered from one tag.
    [Fact]
    public void Unions_across_tags()
    {
        var behaviours = ObjectBehaviors.Of(WithTags(
            ("SpaceBehavior", "SPECIAL_WEAPON"),
            ("LandBehavior", "SELECTABLE")));

        Assert.Contains("SPECIAL_WEAPON", behaviours);
        Assert.Contains("SELECTABLE", behaviours);
    }

    [Fact]
    public void Has_is_case_insensitive_like_the_engine()
    {
        var obj = WithTags(("SpaceBehavior", "special_weapon"));

        Assert.True(ObjectBehaviors.Has(obj, "SPECIAL_WEAPON"));
        Assert.False(ObjectBehaviors.Has(obj, "GARRISON_VEHICLE"));
    }

    [Fact]
    public void Ignores_tags_that_are_not_behaviour_lists()
    {
        var behaviours = ObjectBehaviors.Of(WithTags(("Max_Speed", "10")));

        Assert.Empty(behaviours);
    }

    // An object the index does not know has no behaviours to report - and must not read as "has
    // none of them", which would turn every unresolved reference into a second false diagnostic.
    [Fact]
    public void Unfound_object_yields_nothing()
    {
        var missing = new EffectiveObject(
            "Nope", null, false, false, null,
            ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

        Assert.Empty(ObjectBehaviors.Of(missing));
    }
}
