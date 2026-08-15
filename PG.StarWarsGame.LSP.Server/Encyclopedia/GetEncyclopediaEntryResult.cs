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
/// <param name="IconDataUri">
///     The referenced unit's portrait, drawn in its Against slot. Resolved through the SAME path as
///     the card's own portrait - find the unit the list names, then read its <c>Icon_Name</c> -
///     missing-icon placeholder included, so a unit with no art looks identical wherever it is
///     drawn. Null only when the unit names no icon at all, or is unknown.
/// </param>
public sealed record EncyclopediaReference(
    string ObjectId, string? DisplayName, string? IconDataUri = null);

/// <summary>
///     One active ability from <c>Unit_Abilities_Data</c>, in the order the XML declares them.
/// </summary>
/// <param name="Type">The ability type, e.g. <c>DEFEND</c> or <c>POWER_TO_WEAPONS</c>.</param>
/// <param name="AbilityName">
///     <c>GUI_Activated_Ability_Name</c> - the entry it cross-references in the object's
///     <c>Abilities</c> list. Null for an ability with no command-bar activation.
/// </param>
/// <param name="AlternateIconName">
///     <c>Alternate_Icon_Name</c>. The <c>Alternate_*</c> family - icon, <c>Alternate_Name_Text</c>
///     and <c>Alternate_Description_Text</c> - REPLACES the default it shadows rather than adding a
///     second state, and does so per ability instance, so two units can give the same ability type
///     different icons. When present it is therefore the ability's icon outright. Null for the
///     overwhelming majority of abilities, which fall back to the engine's hardcoded default.
/// </param>
/// <param name="IconDataUri">
///     The ability's icon as a <c>data:image/png;base64,...</c> URI, or <see langword="null" /> when
///     none was found. Resolved by guessing <c>I_SA_&lt;TYPE&gt;</c> against the mega texture -
///     nothing in the game's data links an ability to its icon, so the engine's mapping cannot be
///     read. The guess is validated by the lookup, so a miss shows no icon rather than a wrong one.
/// </param>
/// <param name="Icon">
///     The slot's artwork with its natural size, which the card needs: the ability scale is applied
///     to the icon's own dimensions, and a mod may ship these at other than the base game's 26.
/// </param>
public sealed record EncyclopediaAbility(
    string Type, string? AbilityName, string? AlternateIconName, EncyclopediaImage? Icon = null);

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
/// <param name="Abilities">
///     Active abilities in declaration order. Every entry is returned, not just the two the popup
///     slots: hiding the rest is a display rule, and modders deliberately park auto-activated
///     abilities past the second slot, so the extras are meaningful rather than an error.
/// </param>
/// <param name="GoodAgainst">Targets from <c>Encyclopedia_Good_Against</c>.</param>
/// <param name="VulnerableTo">Targets from <c>Encyclopedia_Vulnerable_To</c>.</param>
/// <param name="Layout">
///     Geometry, fonts and colours for the card. Travels with the text rather than on its own
///     request so the panel can never draw one against a stale copy of the other.
/// </param>
/// <param name="Icon">The object's portrait, or <see langword="null" /> when none resolved.</param>
/// <param name="ShipNames">
///     The pool of individual ship names this object draws from, or <see langword="null" /> when it
///     is not registered for them - which is nearly every object.
///     <para>
///         The server does NOT pick one. Which name to show is a presentation choice with a
///         lifetime - the panel keeps its pick so the card does not reshuffle on every refresh -
///         and a server that picked per request would fight that. <paramref name="UnitClass" />
///         therefore always holds the object's real class line; the client substitutes.
///     </para>
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
    EncyclopediaLayout Layout,
    EncyclopediaIcon? Icon = null,
    EncyclopediaChrome? Chrome = null,
    EncyclopediaShipNames? ShipNames = null
)
{
    /// <summary>The answer for an unknown object, and for every request while the feature is off.</summary>
    public static GetEncyclopediaEntryResult NotFound(string objectId, EncyclopediaLayout layout)
    {
        return new GetEncyclopediaEntryResult(
            false, objectId, null, null, null, [], false, null, [], [], [], layout);
    }
}

/// <summary>
///     The pool of individual ship names an object draws from, reported alongside the one that was
///     drawn.
/// </summary>
/// <remarks>
///     Wired up in GameConstants' <c>ShipNameTextFiles</c>. The engine picks a name and remembers
///     which are spent so a fleet never repeats one; a preview has no campaign to remember for, so
///     it simply picks - which is why the whole pool travels, letting the panel show what else the
///     object could have been called.
/// </remarks>
/// <param name="SourcePath">The path as written in the tag.</param>
/// <param name="FileFound">
///     Whether the file was readable. Separates "wired up but the file is missing" - an authoring
///     mistake - from "wired up and empty", which a bare count cannot express.
/// </param>
/// <param name="Names">Every name in the pool, in file order.</param>
public sealed record EncyclopediaShipNames(
    string SourcePath, bool FileFound, IReadOnlyList<string> Names);

/// <summary>A resolved icon, ready for the client to drop straight into an <c>img</c> element.</summary>
/// <param name="Name">The icon name as written in <c>Icon_Name</c>.</param>
/// <param name="DataUri">
///     A complete <c>data:image/png;base64,...</c> URI. The server encodes rather than shipping raw
///     bytes so the webview needs no decoding step and no blob URLs, which its content security
///     policy would block anyway.
/// </param>
/// <param name="Source">
///     Which layer supplied the pixels - the workspace's own mega texture, a raw source image, or
///     the baked base game. Lets the panel show provenance instead of leaving the author guessing
///     why an icon looks unfamiliar.
/// </param>
/// <param name="IsMegaTextureStale">
///     True when the icon was found only as a raw source while the workspace's mega texture lacks
///     it: drawn, but never repacked, so the game would not show it yet.
/// </param>
/// <param name="Width">Natural width in pixels, or 0 when unknown.</param>
/// <param name="Height">
///     Natural height in pixels, or 0 when unknown. The card draws the portrait at a fixed 50-unit
///     box so this is informational there, but ability icons are scaled from their natural size and
///     a mod may ship them at other than the base game's 26.
/// </param>
public sealed record EncyclopediaIcon(
    string Name, string DataUri, string Source, bool IsMegaTextureStale, int Width = 0, int Height = 0);

/// <summary>
///     The popup's own chrome, cut from the mega texture: the pieces the engine draws the card out
///     of rather than the artwork of any particular unit.
/// </summary>
/// <remarks>
///     <para>
///         Every field is a <c>data:image/png;base64,...</c> URI or <see langword="null" /> when that
///         entry is absent from the atlas. Null is normal, not an error - a mod may ship a mega
///         texture without them - and the card keeps its calibrated CSS rendition as the fallback,
///         so chrome is strictly an upgrade over what it drew before.
///     </para>
///     <para>
///         Sizes are fixed by the atlas and confirm the geometry we originally derived from
///         screenshots: the top bar is 262x20 and 262 is the card's own width, the against frame is
///         129x43 against a derived 127.5x42, and its slot is a 43x43 square.
///     </para>
/// </remarks>
/// <param name="Background">
///     <c>E_BACKGROUND</c>, a 48x48 TILE rather than a full-card image - it repeats to fill the
///     backdrop, so it must not be stretched.
/// </param>
/// <param name="TopBar">
///     <c>E_TOPBAR</c>, the full-width 262x20 header band, WITH the population blip's black disc
///     baked into its left end at x 1..19, y 1..18. The disc is artwork, not something the client
///     should draw: a scan of the band's pixels finds it opaque black and round, matching the
///     18-unit blip measured off a screenshot to within a unit.
/// </param>
/// <param name="TopBarNoBlip">
///     <c>E_TOPBAR2</c>, the same band at the same 262x20 with NO disc - the identical size of the
///     two variants was long unexplained, and this is the explanation. The engine has one band per
///     case, so an object without a <c>Population_Value</c> takes this one and the header shifts
///     left into the freed space.
/// </param>
/// <param name="Line"><c>E_LINE</c>, the separator under the class row.</param>
/// <param name="AgainstFrame"><c>E_AGAINST_FRAME</c>, one Strong/Weak Against panel.</param>
/// <param name="UnitAgainst"><c>E_UNIT_AGAINST</c>, a single slot inside that panel.</param>
/// <param name="FactionFrames">
///     The per-faction frames drawn over the card, one entry per slot the component declares. Always
///     listed even when the art is missing, because the slots come from the XML rather than from the
///     atlas.
/// </param>
public sealed record EncyclopediaChrome(
    EncyclopediaImage? Background,
    EncyclopediaImage? TopBar,
    EncyclopediaImage? TopBarNoBlip,
    EncyclopediaImage? Line,
    EncyclopediaImage? AgainstFrame,
    EncyclopediaImage? UnitAgainst,
    IReadOnlyList<EncyclopediaFactionFrame> FactionFrames);

/// <summary>
///     One faction's frame for the card - an entry of <c>encyclopedia_back</c>'s
///     <c>Icon_Alternate_Texture_Name</c>, which on this component indexes by faction slot.
/// </summary>
/// <remarks>
///     The engine picks the slot from the viewing player's faction; a preview has no player, so the
///     server ships every slot and the client switches between them locally. That also keeps the
///     switch instant and spares a round trip per click.
/// </remarks>
/// <param name="Slot">The index into the list, which is the faction slot the engine selects by.</param>
/// <param name="TextureName">The name as written in the tag, so a slot is identifiable without art.</param>
/// <param name="SlotName">
///     A display name for the slot, or <see langword="null" /> when there is nothing but a guess to
///     offer. See <see cref="SlotNameFor" />.
/// </param>
/// <param name="Image">The artwork, or <see langword="null" /> when the atlas has no such entry.</param>
public sealed record EncyclopediaFactionFrame(
    int Slot, string TextureName, string? SlotName, EncyclopediaImage? Image)
{
    /// <summary>
    ///     The slot names the base game's own data confirms, in slot order.
    /// </summary>
    /// <remarks>
    ///     Deliberately only two. The shipped textures are named <c>i_tooltip_rebel_frame</c> and
    ///     <c>i_tooltip_empire_frame</c>, so those two slots are established by the data itself and
    ///     cannot be wrong. Nothing establishes a third: the base game ships no further frame, and a
    ///     mod's extra slot belongs to whichever faction that mod added, so a name here would be a
    ///     guess dressed as a fact. The engine's one known faction-icon order (<c>FleetIconEnum</c>:
    ///     GOOD, EVIL, PIRATE, UNDERWORLD, BIG_SLUG, RAID) is evidence for <c>g_planet_fleet</c>, not
    ///     for this component, and would be wrong for most mods regardless.
    /// </remarks>
    private static readonly string[] KnownSlotNames = ["Rebel", "Empire"];

    /// <summary>The display name for a slot, or null when only a guess is available.</summary>
    public static string? SlotNameFor(int slot)
    {
        return slot >= 0 && slot < KnownSlotNames.Length ? KnownSlotNames[slot] : null;
    }
}

/// <summary>
///     A piece of artwork with the size it was authored at.
/// </summary>
/// <remarks>
///     <para>
///         The dimensions are the point. Every one of these is atlas artwork a mod can reskin, and
///         the card lays out AROUND them - the header band's height, the Against panel's aspect, the
///         separator's thickness. Sending pixels alone forced the client to assume the base game's
///         sizes, so a mod shipping a taller top bar had it squashed back to 20 units.
///     </para>
///     <para>
///         Dimensions are read from the PNG header, so they describe what was actually cut rather
///         than what a directory record claimed.
///     </para>
/// </remarks>
/// <param name="DataUri">
///     A complete <c>data:image/png;base64,...</c> URI. The server encodes rather than shipping raw
///     bytes so the webview needs no decoding step and no blob URLs, which its content security
///     policy would block anyway.
/// </param>
/// <param name="Width">Natural width in pixels, or 0 when the header could not be read.</param>
/// <param name="Height">Natural height in pixels, or 0 when the header could not be read.</param>
public sealed record EncyclopediaImage(string DataUri, int Width, int Height);
