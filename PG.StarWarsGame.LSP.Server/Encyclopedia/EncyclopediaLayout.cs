// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Encyclopedia;

/// <summary>A colour as the game writes it: four 0-255 channels, alpha last.</summary>
public sealed record EncyclopediaRgba(int R, int G, int B, int A);

/// <summary>
///     Horizontal alignment of a text component. The game encodes this as the *presence* of
///     <c>Left_Justified</c> / <c>Right_Justified</c>: a component carrying neither is centred, so
///     this is resolved here rather than shipping two booleans the client would have to
///     re-interpret.
/// </summary>
/// <remarks>
///     String constants rather than a C# enum, matching <c>LocCategory</c>: Newtonsoft serialises a
///     bare enum as its ordinal, which would put an unlabelled <c>0</c> on the wire and silently
///     shift meaning the day a member is inserted.
/// </remarks>
public static class EncyclopediaTextAlignment
{
    /// <summary>Neither justify tag is set - the shipped <c>encyclopedia_center_text</c> case.</summary>
    public const string Center = "center";

    public const string Left = "left";
    public const string Right = "right";
}

/// <summary>How one text row of the popup is drawn.</summary>
/// <param name="Component">The CommandBarComponent name this came from, for diagnostics.</param>
/// <param name="FontName">Font family, e.g. <c>Arial</c> or <c>Arial Bold</c>.</param>
/// <param name="FontPointSize">Point size before <paramref name="Scale" />.</param>
/// <param name="Scale">Multiplier the engine applies to the point size.</param>
/// <param name="TextColor">Glyph colour.</param>
/// <param name="Alignment">Resolved from the justify tags' presence.</param>
/// <param name="WrapChars">
///     How many CHARACTERS fit on one line, from this component's own <c>Size</c> X.
///     <para>
///         The engine wraps this text on a character count, comparing the line's length against
///         this number - it measures no glyphs at all. That is why a mod can author a run of
///         <c>=</c> that spans the card exactly and rely on it never breaking: they counted
///         characters, and so does the game. The cut fires when a line REACHES the budget, so the
///         longest line it can produce is one character short of it.
///     </para>
///     <para>
///         Not to be confused with <see cref="EncyclopediaLayout.Width" />, which is the backdrop's
///         geometry and has no bearing on where lines break. A mod that widens the card widens this
///         separately - EaWX moves the card from 262 to 340 and the body budget from 41 to 56.
///     </para>
/// </param>
/// <param name="FontUnits">
///     The glyph height in CARD units - the same units <see cref="EncyclopediaLayout.Width" /> is
///     given in, so a client may draw at any zoom and keep the proportions.
///     <para>
///         Derived from the target screen the way the game derives it, truncations included, rather
///         than from a fitted multiplier on <paramref name="FontPointSize" />. See
///         <see cref="EncyclopediaGlyphSize" /> for why a single multiplier cannot be right at two
///         resolutions at once. <paramref name="Scale" /> is already applied.
///     </para>
/// </param>
public sealed record EncyclopediaTextStyle(
    string Component,
    string FontName,
    double FontPointSize,
    double Scale,
    EncyclopediaRgba TextColor,
    string Alignment,
    int WrapChars,
    double FontUnits
);

/// <summary>
///     The popup's geometry and per-row styling, read from the <c>encyclopedia_*</c>
///     CommandBarComponents.
/// </summary>
/// <remarks>
///     <paramref name="Width" /> is the value that matters most: the engine wraps proportional text
///     to it, so it - not a monospace grid - is what produces the line breaks the modder tuned their
///     "=====" bars against. Width and point size scale together, so a client may render at any
///     multiple and keep identical wrap points.
/// </remarks>
/// <param name="Width">Popup width in UI units, from <c>encyclopedia_back</c>'s <c>Size</c> X.</param>
/// <param name="RowHeight">Single-row height, from the same tag's Y.</param>
/// <param name="OffsetX">Content inset from the frame's left edge.</param>
/// <param name="OffsetY">Content inset from the frame's top edge.</param>
/// <param name="IconScale">
///     The factor the header draws the unit icon at, from <c>encyclopedia_icon</c>'s <c>Size</c> X
///     (the shipped file comments that tag as "X is the scale of the icon in the unit header").
///     Stock is 0.75, and the header icon measures 50 units - so 0.75 is the popup's REFERENCE
///     scale rather than a plain multiplier on the asset, and a client should read a scale relative
///     to it. See <paramref name="AbilityIconScale" />, which the same reading sizes correctly.
/// </param>
/// <param name="AbilityIconScale">
///     The factor the class row draws each ability icon at, from the same tag's Y - commented in
///     the shipped file as "Y is the scale of the ability icon". Stock is 0.66 against an ability
///     asset of 26 units, which at the reference scale above puts the slot at 26 * 0.66 / 0.75, or
///     about 23 units.
/// </param>
/// <param name="BackdropColor">Tint applied to the frame behind the text.</param>
/// <param name="BackdropTextureName">
///     <c>encyclopedia_back</c>'s <c>Blank_Texture_Name</c> - the atlas entry the card's backdrop is
///     drawn from. Data rather than a constant: unlike <c>E_TOPBAR</c>, <c>E_LINE</c>,
///     <c>E_AGAINST_FRAME</c> and <c>E_UNIT_AGAINST</c>, which appear in no shipped XML and so are
///     genuinely engine-fixed, this one is named in the file and a reskin renames it.
/// </param>
/// <param name="FactionFrameTextureNames">
///     <c>encyclopedia_back</c>'s <c>Icon_Alternate_Texture_Name</c>, in list order - the faction
///     frames drawn over the card, one per faction slot.
/// </param>
/// <param name="Header">The name row (<c>encyclopedia_header_text</c>).</param>
/// <param name="Body">The body rows (<c>encyclopedia_text</c>).</param>
/// <param name="RightText">Right-hand rows (<c>encyclopedia_right_text</c>).</param>
/// <param name="CenterText">Centred rows (<c>encyclopedia_center_text</c>).</param>
/// <param name="CostText">Cost row (<c>encyclopedia_cost_text</c>).</param>
public sealed record EncyclopediaLayout(
    double Width,
    double RowHeight,
    double OffsetX,
    double OffsetY,
    double IconScale,
    double AbilityIconScale,
    EncyclopediaRgba BackdropColor,
    string BackdropTextureName,
    IReadOnlyList<string> FactionFrameTextureNames,
    EncyclopediaTextStyle Header,
    EncyclopediaTextStyle Body,
    EncyclopediaTextStyle RightText,
    EncyclopediaTextStyle CenterText,
    EncyclopediaTextStyle CostText,
    EncyclopediaOffsets Offsets
);