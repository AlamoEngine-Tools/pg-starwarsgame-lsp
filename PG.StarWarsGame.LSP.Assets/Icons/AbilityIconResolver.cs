// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>What a lookup could establish about an ability's command-bar icon.</summary>
public enum AbilityIconOutcome
{
    /// <summary>
    ///     Nothing can be asserted: no catalog, or the type-name guess missed. See
    ///     <see cref="AbilityIconResolver" /> for why a miss here is not evidence of missing art.
    /// </summary>
    Unknown,

    /// <summary>The icon was found. <see cref="AbilityIcon.Icon" /> carries it.</summary>
    Resolved,

    /// <summary>
    ///     The ability names an icon in its own data and that icon is not there. The caller should
    ///     draw the missing-icon placeholder, because that is what the engine draws.
    /// </summary>
    DeclaredButMissing
}

/// <param name="Icon">The pixels, when and only when <paramref name="Outcome" /> is Resolved.</param>
public sealed record AbilityIcon(IconResolution? Icon, AbilityIconOutcome Outcome)
{
    public static AbilityIcon Unknown { get; } = new(null, AbilityIconOutcome.Unknown);

    public static AbilityIcon Missing { get; } = new(null, AbilityIconOutcome.DeclaredButMissing);

    public static AbilityIcon Found(IconResolution icon) => new(icon, AbilityIconOutcome.Resolved);
}

/// <summary>
///     Which command-bar icon an ability draws.
/// </summary>
/// <remarks>
///     <para>
///         Shared by the encyclopedia card and the model preview's Gameplay lens. It was private to
///         the former until the latter needed the same answer, and two copies of this precedence
///         would be two places for it to drift - the same shape of bug as an editor and a preview
///         disagreeing about whether a piece of art exists.
///     </para>
///     <para>
///         The order, and why:
///     </para>
///     <list type="number">
///         <item>
///             <c>Alternate_Icon_Name</c>, when the ability declares one. An <c>Alternate_*</c> tag
///             REPLACES the default it shadows, so the type-name guess never applies. It is per
///             INSTANCE, not per type: two units can give one ability type different icons.
///         </item>
///         <item>
///             A confirmed exception from <see cref="AbilityIconNames" />. These were checked against
///             the running game rather than inferred; several could not be guessed at all.
///         </item>
///         <item>
///             <c>I_SA_&lt;TYPE&gt;</c>, which most abilities happen to follow.
///         </item>
///     </list>
/// </remarks>
public static class AbilityIconResolver
{
    /// <param name="catalog">The project's icons; <see langword="null" /> when unavailable.</param>
    /// <param name="type">The ability's <c>Type</c>.</param>
    /// <param name="alternateIconName">Its <c>Alternate_Icon_Name</c>, if it declares one.</param>
    public static AbilityIcon Resolve(IconCatalog? catalog, string type, string? alternateIconName)
    {
        if (catalog is null || string.IsNullOrWhiteSpace(type))
            return AbilityIcon.Unknown;

        if (!string.IsNullOrWhiteSpace(alternateIconName))
        {
            var overridden = catalog.Resolve(alternateIconName.Trim());

            // A declared override that is not there draws the placeholder - the engine would draw
            // missing art, so showing it is fidelity. Falling back to the default here would hide a
            // broken reference behind a plausible icon the game will never show.
            return overridden is null ? AbilityIcon.Missing : AbilityIcon.Found(overridden);
        }

        var known = AbilityIconNames.For(type);
        if (known is not null)
        {
            var mapped = catalog.Resolve(known);
            if (mapped is not null)
                return AbilityIcon.Found(mapped);
        }

        // A miss HERE is AMBIGUOUS, which is why it reports neither the icon nor the placeholder.
        // The engine resolves a hardcoded name out of the same mega texture, so a missing entry DOES
        // make it draw missing - but I_SA_<TYPE> is only a guess at that name, and a miss can equally
        // mean the real name is something else entirely (BARRAGE's icon is I_SA_BARRAGE_AREA) while
        // the game shows perfectly good art. Those cannot be told apart without knowing the
        // hardcoded name, so the caller asserts neither and falls back to the ability type as text.
        var resolved = catalog.Resolve("I_SA_" + type.Trim().ToUpperInvariant());
        return resolved is null ? AbilityIcon.Unknown : AbilityIcon.Found(resolved);
    }
}
