// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Encyclopedia;

/// <summary>
///     Where the popup's header pieces sit, from <c>GameConstants</c>.
/// </summary>
/// <remarks>
///     <para>
///         These are card-relative positions in the card's own units. Before placing a header
///         piece the engine subtracts <c>encyclopedia_back</c>'s <c>Offset</c> X (stock 5) from it,
///         and it does so for EVERY piece, the full-width band included - so the subtraction is a
///         uniform shift of the whole content block, not a per-row adjustment. It cancels because
///         the back itself is drawn at that same <c>Offset</c>, which moves the card's left edge by
///         the identical amount. Net: the value in the file is the position measured from the
///         card's left edge, and nothing here has to know the offset exists.
///     </para>
///     <para>
///         <c>encyclopedia_icon</c>'s own <c>Default_Offset</c> is NOT applied. Its shipped comment
///         reads "X is the number of pixels to move the unit icon", but the encyclopedia setup path
///         reads <c>Default_Offset</c> only from the BACK component; the icon's is never fetched.
///         The stock -2 therefore moves nothing, and reproducing it would put the portrait two
///         pixels left of where the game draws it.
///     </para>
///     <para>
///         What the position MEANS follows the row's justification, which is how the same number
///         serves three different jobs: a left-justified row (the name, the class) is placed by its
///         LEFT edge, an icon or a centred row (the portrait, the blip's number) by its CENTRE, and
///         a right-justified row (the cost) by its RIGHT edge.
///     </para>
///     <para>
///         Data, not constants. A mod that moves the blip or the portrait by editing GameConstants
///         moves them in the game, and the card followed the base game's values until this was read.
///     </para>
/// </remarks>
/// <param name="Population">The blip and its number.</param>
/// <param name="Name">The name row.</param>
/// <param name="Cost">The cost row, right-justified.</param>
/// <param name="IconX">The portrait's centre.</param>
/// <param name="IconY">The portrait's vertical nudge; negative lifts it.</param>
/// <param name="ClassY">The class row's vertical offset.</param>
public sealed record EncyclopediaOffsets(
    int Population,
    int Name,
    int Cost,
    int IconX,
    int IconY,
    int ClassY)
{
    /// <summary>The values Empire at War ships, which EaWX keeps unchanged.</summary>
    public static EncyclopediaOffsets Shipped { get; } = new(11, 68, 258, 39, -12, 5);

    /// <summary>
    ///     The name row's position, given what else is drawn on that row.
    /// </summary>
    /// <remarks>
    ///     Nothing is left holding empty space: with no blip the name moves left by the blip's own
    ///     offset, and with no portrait it closes the portrait's share of the gap as well. With
    ///     neither, it simply starts where the blip would have.
    /// </remarks>
    public int NameFor(bool hasBlip, bool hasIcon)
    {
        if (!hasIcon && !hasBlip) return Population;
        if (!hasBlip) return Name - Population;
        if (!hasIcon) return Name - (IconX - Population);
        return Name;
    }

    /// <summary>The portrait's centre, which also moves left into the blip's space when there is none.</summary>
    public int IconXFor(bool hasBlip)
    {
        return hasBlip ? IconX : IconX - Population;
    }
}