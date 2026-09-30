// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Encyclopedia;

namespace PG.StarWarsGame.LSP.Server.Tests.Encyclopedia;

/// <summary>
///     The two popup rows whose wrap budget is NOT simply their component's <c>Size</c> X.
/// </summary>
/// <remarks>
///     Every row of the popup wraps on a character budget, and most of them take it from the
///     component they are drawn with. The name and the class line do not: both share their row with
///     something else, and the budget moves to make room for it.
/// </remarks>
public sealed class EncyclopediaRowBudgetsTest
{
    // ── the name row ─────────────────────────────────────────────────────────

    /// <summary>
    ///     With a cost drawn beside it, the name gets exactly what the component declares.
    /// </summary>
    [Fact]
    public void Name_WithACost_IsTheComponentBudget()
    {
        Assert.Equal(40, EncyclopediaRowBudgets.Name(40, true));
        Assert.Equal(23, EncyclopediaRowBudgets.Name(23, true));
    }

    /// <summary>
    ///     With no cost there is nothing sharing the row, and the name is given eight more
    ///     characters of it.
    /// </summary>
    [Fact]
    public void Name_WithNoCost_GainsEight()
    {
        Assert.Equal(48, EncyclopediaRowBudgets.Name(40, false));
        Assert.Equal(31, EncyclopediaRowBudgets.Name(23, false));
    }

    // ── the class row ────────────────────────────────────────────────────────

    /// <summary>
    ///     The class line starts from a fixed 32 rather than from its component, because it is drawn
    ///     with the body's component but is not the body.
    /// </summary>
    [Fact]
    public void UnitClass_WithNoAbilities_IsThirtyTwo()
    {
        Assert.Equal(32, EncyclopediaRowBudgets.UnitClass(0));
    }

    /// <summary>
    ///     Each ability icon sits on this row and takes four characters' worth of it.
    /// </summary>
    [Fact]
    public void UnitClass_LosesFourCharactersPerAbilityDrawn()
    {
        Assert.Equal(28, EncyclopediaRowBudgets.UnitClass(1));
        Assert.Equal(24, EncyclopediaRowBudgets.UnitClass(2));
    }

    /// <summary>
    ///     Only the slots that are actually drawn cost anything. A unit may declare many abilities -
    ///     modders park auto-activated ones past the visible slots deliberately - and those are not
    ///     on this row, so they do not narrow it.
    /// </summary>
    [Fact]
    public void UnitClass_CountsOnlyTheDrawnSlots()
    {
        Assert.Equal(
            EncyclopediaRowBudgets.UnitClass(EncyclopediaRowBudgets.DrawnAbilitySlots),
            EncyclopediaRowBudgets.UnitClass(9));
    }

    /// <summary>
    ///     A budget can never reach zero through this path, but the guard is here rather than
    ///     assumed: a budget of nothing would put one character on every line.
    /// </summary>
    [Fact]
    public void UnitClass_NeverCollapses()
    {
        Assert.True(EncyclopediaRowBudgets.UnitClass(100) > 0);
    }
}