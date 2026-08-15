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

    /// <summary>
    ///     The two entries derived from an orphan pairing rather than observed in game.
    /// </summary>
    /// <remarks>
    ///     Kept as their own case so the distinction survives: no <c>Unit_Ability</c> has type
    ///     <c>CONTAMINATE</c> or <c>REPAIR_VEHICLE</c>, so those atlas entries are unclaimed, and the
    ///     only contaminate-flavoured and repair-flavoured abilities that reach the command bar at
    ///     all are these two. That is a much narrower argument than name similarity, but it is still
    ///     an inference - see section 6 of docs/ability-icon-mapping.md.
    /// </remarks>
    [Theory]
    [InlineData("RADIOACTIVE_CONTAMINATE", "I_SA_CONTAMINATE.TGA")]
    [InlineData("TARGETED_REPAIR", "I_SA_REPAIR_VEHICLE.TGA")]
    public void For_ReturnsTheInferredOrphanPairing(string type, string expected)
    {
        Assert.Equal(expected, AbilityIconNames.For(type));
    }

    /// <summary>
    ///     Ability types proven to never reach the command bar must NOT be given an icon.
    /// </summary>
    /// <remarks>
    ///     None of these carries a <c>GUI_Activated_Ability_Name</c> on any instance in the shipped
    ///     data, which is what binds an ability to a command-bar button - so the game never draws
    ///     one, and inventing a mapping would put art where the game shows none.
    /// </remarks>
    [Theory]
    [InlineData("AREA_EFFECT_CONVERT")]
    [InlineData("AREA_EFFECT_STUN")]
    [InlineData("EJECT_VEHICLE_THIEF")]
    [InlineData("FIRE_LOBBING_SUPERWEAPON")]
    [InlineData("TARGETED_INVULNERABILITY")]
    [InlineData("UNTARGETED_STICKY_BOMB")]
    public void For_ReturnsNullForAbilitiesThatNeverReachTheCommandBar(string type)
    {
        Assert.Null(AbilityIconNames.For(type));
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
        Assert.Equal(20, AbilityIconNames.All.Count());
    }
}
