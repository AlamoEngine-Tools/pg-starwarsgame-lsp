// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     What a mode row that never applies says. A unit-mode row is read for the first two ability
///     slots only; the rocket row is read whenever the unit has an active ROCKET_ATTACK, in any slot -
///     the game keeps an ability's state by its type, filled from every entry of the list. The rocket
///     row used to borrow the slot sentence, naming a limit the game does not have.
/// </summary>
public sealed class HardpointModeRowWithoutAbilityHandlerTest
{
    private static readonly HardpointModeRowWithoutAbilityHandler Sut = new();

    private static HardpointModeRowWithoutAbilityFact Fact(string tag, string ability)
    {
        return new HardpointModeRowWithoutAbilityFact("file:///x.xml", 3, 2, 10, "HP_A", tag, ability, "SHIP", -1,
            ["DEFEND"]);
    }

    [Fact]
    public void TheRocketRow_NamesNoSlotLimit()
    {
        var d = Assert.Single(Sut.Handle(Fact("Fire_When_In_Rocket_Attack_Mode", "ROCKET_ATTACK"),
            XmlHandlerTestFixtures.EmptyCtx).ToList());

        Assert.DoesNotContain("slot", d.Message);
        Assert.Contains("ROCKET_ATTACK", d.Message);
    }

    [Fact]
    public void AUnitModeRow_KeepsTheTwoSlotLimit()
    {
        var d = Assert.Single(Sut.Handle(Fact("Defend_Mode_Weapon_Delay_Multiplier", "DEFEND"),
            XmlHandlerTestFixtures.EmptyCtx).ToList());

        Assert.Contains("first or second slot", d.Message);
    }
}