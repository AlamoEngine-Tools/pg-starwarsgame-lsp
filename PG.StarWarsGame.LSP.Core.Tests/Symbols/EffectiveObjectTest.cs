// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class EffectiveObjectTest
{
    private static EffectiveObject With(params (string Tag, string Value)[] tags)
    {
        return new EffectiveObject("Obj", "GameObjectType", true, false, null, ["Obj"],
            [.. tags.Select(t => new EffectiveTag(t.Tag, t.Value, t.Value, VariantProvenance.Own, "Obj", null))]);
    }

    [Fact]
    public void ValueOf_returns_the_tag_value_trimmed()
    {
        Assert.Equal("1", With(("Special_Weapon_Index", "  1 ")).ValueOf("Special_Weapon_Index"));
    }

    // Element names are case-insensitive to the engine, so the lookup is too.
    [Fact]
    public void ValueOf_matches_the_tag_name_case_insensitively()
    {
        Assert.Equal("1", With(("special_weapon_index", "1")).ValueOf("Special_Weapon_Index"));
    }

    // A repeated scalar: the engine keeps the last write.
    [Fact]
    public void ValueOf_takes_the_last_occurrence()
    {
        Assert.Equal("2",
            With(("Special_Weapon_Index", "1"), ("Special_Weapon_Index", "2")).ValueOf("Special_Weapon_Index"));
    }

    [Fact]
    public void ValueOf_is_null_for_a_tag_the_object_does_not_carry()
    {
        Assert.Null(With(("Tactical_Health", "100")).ValueOf("Special_Weapon_Index"));
    }

    [Fact]
    public void ValueOf_is_null_on_an_object_that_was_not_found()
    {
        var missing = new EffectiveObject("Nope", null, false, false, null,
            ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

        Assert.Null(missing.ValueOf("Special_Weapon_Index"));
    }
}