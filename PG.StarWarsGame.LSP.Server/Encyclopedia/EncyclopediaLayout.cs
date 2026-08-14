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
public sealed record EncyclopediaTextStyle(
    string Component,
    string FontName,
    double FontPointSize,
    double Scale,
    EncyclopediaRgba TextColor,
    string Alignment
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
    EncyclopediaTextStyle Header,
    EncyclopediaTextStyle Body,
    EncyclopediaTextStyle RightText,
    EncyclopediaTextStyle CenterText,
    EncyclopediaTextStyle CostText
);
