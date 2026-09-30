// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Encyclopedia;

/// <summary>
///     The wrap budgets of the two popup rows that do not simply take their component's
///     <c>Size</c> X.
/// </summary>
/// <remarks>
///     <para>
///         Every row wraps on a character budget (see <see cref="EncyclopediaTextStyle.WrapChars" />),
///         and most take it from the component they are drawn with. The name row and the class row
///         are given an explicit one instead, and in both cases for the same reason: something else
///         is drawn on that row, and the budget moves to make room for it.
///     </para>
///     <para>
///         The ARITHMETIC here is measured. Its two INPUTS are read from the object rather than
///         measured through the engine's own call path, and both are recorded on the result so the
///         numbers can be checked against a card: see <c>GetEncyclopediaEntryResult.BuildCost</c>
///         and the ability list.
///     </para>
/// </remarks>
public static class EncyclopediaRowBudgets
{
    /// <summary>What the name row gains when no cost is drawn beside it.</summary>
    private const int NameBonusWithoutCost = 8;

    /// <summary>The class row's budget before any ability icon takes part of it.</summary>
    private const int ClassRowBase = 32;

    /// <summary>What one drawn ability icon costs the class row.</summary>
    private const int ClassRowPerAbility = 4;

    /// <summary>
    ///     How many ability icons the popup draws on the class row. Anything further down an
    ///     object's list is not drawn - modders park auto-activated abilities past these slots on
    ///     purpose - so it takes none of the row.
    /// </summary>
    public const int DrawnAbilitySlots = 2;

    /// <summary>
    ///     The name row's budget.
    /// </summary>
    /// <param name="componentBudget"><c>encyclopedia_header_text</c>'s own <c>Size</c> X.</param>
    /// <param name="hasCost">
    ///     Whether a cost is drawn on this row. The engine keys this on the cost it was handed;
    ///     here it is the object's declared build cost, which is the same thing for the galactic
    ///     card this previews and is reported alongside so it can be checked.
    /// </param>
    public static int Name(int componentBudget, bool hasCost)
    {
        return hasCost ? componentBudget : componentBudget + NameBonusWithoutCost;
    }

    /// <summary>
    ///     The class row's budget, narrowed by each ability icon sharing it.
    /// </summary>
    /// <param name="abilityCount">
    ///     How many abilities the object declares. Only the drawn slots count, so anything past
    ///     <see cref="DrawnAbilitySlots" /> makes no difference.
    /// </param>
    public static int UnitClass(int abilityCount)
    {
        var drawn = Math.Clamp(abilityCount, 0, DrawnAbilitySlots);

        // Cannot reach zero from a clamped count, but a budget of nothing would put one character
        // on every line, so it is guarded rather than assumed.
        return Math.Max(1, ClassRowBase - drawn * ClassRowPerAbility);
    }
}
