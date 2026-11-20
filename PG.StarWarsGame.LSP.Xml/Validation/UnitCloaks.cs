// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Abilities;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>One cloak a unit's type carries in its <c>Abilities</c> list.</summary>
/// <param name="Element">The block's element as the game spells it: <c>Stealth_Ability</c> or <c>Force_Cloak_Ability</c>.</param>
/// <param name="IsForceCloak">Whether this is the Force cloak, which leaves attached models drawn.</param>
/// <param name="Colour">The block's colour as written, or null when it writes none.</param>
/// <param name="TransitionSeconds">The block's transition time as the game reads it, or null when it writes none.</param>
/// <param name="LookChangesNothing">
///     Whether the cloak's colour and fade leave the unit looking as it did: a white opaque colour, or
///     no transition time.
/// </param>
public sealed record UnitCloak(
    string Element,
    bool IsForceCloak,
    string? Colour,
    float? TransitionSeconds,
    bool LookChangesNothing);

/// <summary>
///     The cloaks a resolved object carries: the first <c>Stealth_Ability</c> and the first
///     <c>Force_Cloak_Ability</c> block of its effective <c>Abilities</c> list.
/// </summary>
/// <remarks>
///     <para>
///         The one reading of a cloak, shared by the preview and the cloak diagnostics so the two never
///         disagree on a unit's look: the list's FRAGMENT through <see cref="UnitAbilityReader.ReadBlocks" />,
///         the first block of each element, an unwritten or unreadable colour as white opaque and an
///         unwritten time as 0.
///     </para>
///     <para>
///         Measured in the game: the look is the colour mixed in by the fade, and only while the type
///         carries the block. A white opaque colour therefore changes nothing. A transition time of 0
///         showing nothing at all is inferred, not measured.
///     </para>
/// </remarks>
public static class UnitCloaks
{
    public const string StealthElement = "Stealth_Ability";
    public const string ForceCloakElement = "Force_Cloak_Ability";

    private const string AbilitiesTag = "Abilities";

    private static readonly (string Element, string Colour, string Time, bool Force)[] Blocks =
    [
        (StealthElement, "Stealth_Color", "Stealth_Transition_Time", false),
        (ForceCloakElement, "Force_Cloak_Color", "Force_Cloak_Transition_In_Secs", true)
    ];

    private static readonly char[] ColourSeparators = [' ', '\t', ',', '\r', '\n'];

    public static IReadOnlyList<UnitCloak> Of(EffectiveObject obj)
    {
        var fragment = obj.FragmentOf(AbilitiesTag);
        if (string.IsNullOrWhiteSpace(fragment))
            return [];

        var cloaks = new List<UnitCloak>();

        foreach (var (element, colourTag, timeTag, force) in Blocks)
        {
            var block = UnitAbilityReader.ReadBlocks(fragment, element).FirstOrDefault();
            if (block is null)
                continue;

            var colour = block.Tag(colourTag);
            var time = EngineNumber.ReadFloat(block.Tag(timeTag));
            cloaks.Add(new UnitCloak(element, force, colour, time, IsWhiteOpaque(colour) || !(time > 0)));
        }

        return cloaks;
    }

    /// <summary>The object's <paramref name="element" /> cloak, or null when it carries none.</summary>
    public static UnitCloak? Of(EffectiveObject obj, string element)
    {
        return Of(obj).FirstOrDefault(c => c.Element.Equals(element, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Whether a colour reads as white opaque - which an unwritten or unreadable one does, the way
    ///     the preview falls back to white.
    /// </summary>
    private static bool IsWhiteOpaque(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var tokens = raw.Split(ColourSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
            return true;

        for (var i = 0; i < Math.Min(4, tokens.Length); i++)
        {
            if (!int.TryParse(tokens[i], out var channel))
                return true;
            if (channel < 255)
                return false;
        }

        return true;
    }
}