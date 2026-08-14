// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

public sealed class AbilityIconNamesTest
{
    [Theory]
    [InlineData("BARRAGE", "I_SA_BARRAGE_AREA.TGA")]
    // Plural, where the ability type is singular.
    [InlineData("CAPTURE_VEHICLE", "I_SA_CAPTURE_VEHICLES.TGA")]
    // The shipped atlas misspells it - LIGHTING, not LIGHTNING.
    [InlineData("FORCE_LIGHTNING", "I_SA_FORCE_LIGHTING.TGA")]
    // No naming rule would ever produce this one.
    [InlineData("INVULNERABILITY", "I_SA_EVASIVE_MANEUVERS.TGA")]
    public void For_ReturnsTheConfirmedException(string type, string expected)
    {
        Assert.Equal(expected, AbilityIconNames.For(type));
    }

    [Fact]
    public void For_IsCaseInsensitiveAndTrims()
    {
        Assert.Equal("I_SA_DEFEND_MODE.TGA", AbilityIconNames.For("  defend  "));
    }

    // Types whose icon IS named after them must not be listed - the convention covers those, and a
    // redundant entry would be one more thing to keep in sync.
    [Theory]
    [InlineData("BERSERKER")]
    [InlineData("STEALTH")]
    [InlineData("DRAIN_LIFE")]
    public void For_ReturnsNullForTypesTheConventionAlreadyCovers(string type)
    {
        Assert.Null(AbilityIconNames.For(type));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT_AN_ABILITY")]
    public void For_ReturnsNullForUnknownInput(string type)
    {
        Assert.Null(AbilityIconNames.For(type));
    }

    [Fact]
    public void All_EntriesNameAnAtlasStyleIcon()
    {
        Assert.All(AbilityIconNames.All, entry =>
        {
            Assert.StartsWith("I_SA_", entry.Value, StringComparison.Ordinal);
            Assert.EndsWith(".TGA", entry.Value, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void All_ContainsEveryConfirmedException()
    {
        Assert.Equal(18, AbilityIconNames.All.Count());
    }
}
