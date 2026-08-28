// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

/// <summary>
///     Which localisation keys hold an ability's name and description.
/// </summary>
/// <remarks>
///     Unlike <see cref="AbilityIconNames" />, this needs no recorded table: the key convention was
///     measured against the shipped data and holds for every type. See the class for the numbers.
/// </remarks>
public sealed class AbilityTextKeysTest
{
    [Fact]
    public void NameKey_FollowsTheTooltipConvention()
    {
        Assert.Equal("TEXT_TOOLTIP_ABILITY_DEFEND_NAME", AbilityTextKeys.NameKeyFor("DEFEND"));
    }

    [Fact]
    public void DescriptionKey_FollowsTheTooltipConvention()
    {
        Assert.Equal(
            "TEXT_TOOLTIP_ABILITY_DEFEND_DESCRIPTION", AbilityTextKeys.DescriptionKeyFor("DEFEND"));
    }

    // The XML writes whatever the author typed; the keys are uppercase.
    [Theory]
    [InlineData("defend")]
    [InlineData("Defend")]
    [InlineData("  DEFEND  ")]
    public void TypeIsNormalisedBeforeTheKeyIsBuilt(string written)
    {
        Assert.Equal("TEXT_TOOLTIP_ABILITY_DEFEND_NAME", AbilityTextKeys.NameKeyFor(written));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NoType_YieldsNoKey(string? type)
    {
        Assert.Null(AbilityTextKeys.NameKeyFor(type));
        Assert.Null(AbilityTextKeys.DescriptionKeyFor(type));
    }
}
