// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Encyclopedia;

/// <summary>
///     One line of the popup body: the localisation key that produced it and the text that key
///     resolved to.
/// </summary>
/// <param name="Key">The localisation key as written in <c>Encyclopedia_Text</c>.</param>
/// <param name="Text">
///     The translation, or <see langword="null" /> when the key is absent from the loaded
///     databases. Unresolved lines are kept rather than dropped - a silently shorter popup would
///     hide the very authoring mistake this preview exists to surface.
/// </param>
public sealed record EncyclopediaLine(string Key, string? Text);

/// <summary>
///     A GameObject the popup points at (<c>Encyclopedia_Good_Against</c> /
///     <c>Encyclopedia_Vulnerable_To</c>), with the display name resolved through the target's own
///     <c>Text_ID</c>.
/// </summary>
/// <param name="ObjectId">The referenced object's id, as written in the tag.</param>
/// <param name="DisplayName">
///     The target's resolved name, or <see langword="null" /> when the object is unknown or has no
///     resolvable <c>Text_ID</c>.
/// </param>
public sealed record EncyclopediaReference(string ObjectId, string? DisplayName);

/// <summary>
///     One GUI-activated ability, drawn as a slot in the popup's class row.
/// </summary>
/// <param name="Type">The <c>Type</c> of the <c>Unit_Ability</c>, e.g. <c>LUCKY_SHOT</c>.</param>
/// <param name="AbilityName">
///     <c>GUI_Activated_Ability_Name</c> - the entry it cross-references in the object's
///     <c>Abilities</c> list. Its presence is what makes the ability GUI-activated at all.
/// </param>
/// <param name="AlternateIconName">
///     <c>Alternate_Icon_Name</c>, the icon for the ability's alternate state. This is the only
///     icon the schema defines for an ability: nothing in the shipped data gives a *primary* one,
///     so the ordinary icon appears to be supplied by the engine, presumably keyed off
///     <paramref name="Type" />. Null for the overwhelming majority of abilities.
/// </param>
public sealed record EncyclopediaAbility(string Type, string? AbilityName, string? AlternateIconName);

/// <summary>Result of <c>aet/getEncyclopediaEntry</c>.</summary>
/// <param name="Found">Whether the object id resolved.</param>
/// <param name="ObjectId">The canonical id of the resolved object.</param>
/// <param name="TypeName">The resolved object's GameObject type.</param>
/// <param name="DisplayName">The name line, from <c>Text_ID</c>.</param>
/// <param name="UnitClass">The class line, from <c>Encyclopedia_Unit_Class</c>.</param>
/// <param name="Body">The popup body, one entry per key, in tag order.</param>
/// <param name="UsedMultiplayerBody">
///     Whether <c>MP_Encyclopedia_Text</c> supplied <paramref name="Body" />. Lets the client say
///     which variant it is showing instead of leaving the SP/MP toggle looking inert on objects
///     that carry no MP text.
/// </param>
/// <param name="PopulationValue">
///     <c>Population_Value</c>, drawn as the blip at the popup's top-left, or <see langword="null" />
///     when the object has none - in which case the game shifts the header left into that space.
///     Null rather than 0 for an unparsable value: a wrong number is worse than no blip.
/// </param>
/// <param name="GoodAgainst">Targets from <c>Encyclopedia_Good_Against</c>.</param>
/// <param name="VulnerableTo">Targets from <c>Encyclopedia_Vulnerable_To</c>.</param>
/// <param name="Layout">
///     Geometry, fonts and colours for the card. Travels with the text rather than on its own
///     request so the panel can never draw one against a stale copy of the other.
/// </param>
public sealed record GetEncyclopediaEntryResult(
    bool Found,
    string ObjectId,
    string? TypeName,
    string? DisplayName,
    string? UnitClass,
    IReadOnlyList<EncyclopediaLine> Body,
    bool UsedMultiplayerBody,
    int? PopulationValue,
    IReadOnlyList<EncyclopediaAbility> Abilities,
    IReadOnlyList<EncyclopediaReference> GoodAgainst,
    IReadOnlyList<EncyclopediaReference> VulnerableTo,
    EncyclopediaLayout Layout
)
{
    /// <summary>The answer for an unknown object, and for every request while the feature is off.</summary>
    public static GetEncyclopediaEntryResult NotFound(string objectId, EncyclopediaLayout layout)
    {
        return new GetEncyclopediaEntryResult(
            false, objectId, null, null, null, [], false, null, [], [], [], layout);
    }
}
