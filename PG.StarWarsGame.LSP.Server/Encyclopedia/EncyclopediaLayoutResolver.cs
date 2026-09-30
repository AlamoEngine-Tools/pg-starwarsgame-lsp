// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Encyclopedia;

/// <summary>
///     Reads the popup's geometry, fonts and colours from the <c>encyclopedia_*</c>
///     CommandBarComponents.
/// </summary>
/// <remarks>
///     The base game's shipped values are the floor and every lookup falls back to them per tag, so
///     a mod that re-colours one row without restating its font still renders with the font the
///     game would use. Values are read through <see cref="EffectiveObjectResolver" />, whose tag
///     source already lets a workspace component shadow the baseline one - which is exactly the
///     "mod overrides base game" rule, so nothing here needs to know about layers. Falling back to
///     constants only when the index has nothing keeps the base-game path from drifting away from
///     what is actually on disk.
/// </remarks>
public sealed class EncyclopediaLayoutResolver
{
    private const string BackComponent = "encyclopedia_back";
    private const string IconComponent = "encyclopedia_icon";
    private const string HeaderComponent = "encyclopedia_header_text";
    private const string BodyComponent = "encyclopedia_text";
    private const string RightComponent = "encyclopedia_right_text";
    private const string CenterComponent = "encyclopedia_center_text";
    private const string CostComponent = "encyclopedia_cost_text";

    /// <summary>
    ///     The values Empire at War ships in <c>Data/Xml/Commandbarcomponents.xml</c>. Kept as the
    ///     last resort for workspaces with no indexed component - a preview that drew nothing
    ///     because the file was absent would be less useful than one that draws the stock popup.
    /// </summary>
    /// <summary>
    ///     The faction frames <c>encyclopedia_back</c> ships, in slot order. Exactly two, and that
    ///     is the whole of the base game's faction awareness: the atlas carries no third frame, so
    ///     playing a faction past slot 1 draws none rather than falling back to another faction's.
    /// </summary>
    private static readonly string[] DefaultFactionFrames =
        ["i_tooltip_rebel_frame.tga", "i_tooltip_empire_frame.tga"];

    public static readonly EncyclopediaLayout Defaults = new(
        262d, 14d, 5d, 2d, 0.75d, 0.66d,
        new EncyclopediaRgba(255, 255, 255, 128),
        "e_background.tga",
        DefaultFactionFrames,
        // The trailing number on each row is the line's CHARACTER budget, from that component's own
        // Size X. The shipped body says 41 beside a stale "42 14" comment; 41 is what reproduces
        // the shipped wrap of Luke Skywalker's biography, whose second line lands on exactly 40.
        // The last two numbers on each row are the line's CHARACTER budget and the glyph height in
        // card units. Every resolved style recomputes the second for the target screen; the value
        // here is the default screen's, so a caller holding these defaults alone still has a
        // drawable size rather than a zero.
        new EncyclopediaTextStyle(HeaderComponent, "Arial Bold", 7d, 1.0d,
            new EncyclopediaRgba(255, 255, 255, 255), EncyclopediaTextAlignment.Left, 23,
            DefaultUnits(7d, 1.0d)),
        new EncyclopediaTextStyle(BodyComponent, "Arial", 7d, 1.0d,
            new EncyclopediaRgba(192, 192, 192, 200), EncyclopediaTextAlignment.Left, 41,
            DefaultUnits(7d, 1.0d)),
        new EncyclopediaTextStyle(RightComponent, "Arial", 7d, 1.0d,
            new EncyclopediaRgba(192, 192, 192, 255), EncyclopediaTextAlignment.Right, 42,
            DefaultUnits(7d, 1.0d)),
        new EncyclopediaTextStyle(CenterComponent, "Arial", 7d, 1.0d,
            new EncyclopediaRgba(255, 255, 255, 255), EncyclopediaTextAlignment.Center, 42,
            DefaultUnits(7d, 1.0d)),
        new EncyclopediaTextStyle(CostComponent, "EmpireAtWar-Bold", 7d, 1.0d,
            new EncyclopediaRgba(192, 192, 192, 255), EncyclopediaTextAlignment.Right, 42,
            DefaultUnits(7d, 1.0d)),
        EncyclopediaOffsets.Shipped);

    private static double DefaultUnits(double pointSize, double scale)
    {
        return EncyclopediaGlyphSize.Units(pointSize, scale,
            EncyclopediaGlyphSize.DefaultScreenWidth, EncyclopediaGlyphSize.DefaultScreenHeight);
    }

    private readonly EffectiveObjectResolver _resolver;
    private readonly int _screenHeight;
    private readonly int _screenWidth;

    /// <param name="screenWidth">
    ///     The screen the card is drawn for. The game sizes this popup's glyphs from the display it
    ///     runs on, so the preview has to be told which one to be faithful to; the defaults are the
    ///     common case.
    /// </param>
    /// <param name="screenHeight"><inheritdoc cref="screenWidth" /></param>
    public EncyclopediaLayoutResolver(
        EffectiveObjectResolver resolver,
        int screenWidth = EncyclopediaGlyphSize.DefaultScreenWidth,
        int screenHeight = EncyclopediaGlyphSize.DefaultScreenHeight)
    {
        _resolver = resolver;
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
    }

    public EncyclopediaLayout Resolve()
    {
        var back = TagsOf(BackComponent);
        var (width, rowHeight) = Pair(back, "Size", Defaults.Width, Defaults.RowHeight);
        var (offsetX, offsetY) = Pair(back, "Offset", Defaults.OffsetX, Defaults.OffsetY);

        // encyclopedia_icon's Size is a pair of scales, not a pixel size: X scales the unit icon in
        // the header, Y the ability icons - both per the shipped file's own comment on the tag.
        var (iconScale, abilityIconScale) =
            Pair(TagsOf(IconComponent), "Size", Defaults.IconScale, Defaults.AbilityIconScale);

        return new EncyclopediaLayout(
            width,
            rowHeight,
            offsetX,
            offsetY,
            iconScale,
            abilityIconScale,
            Colour(back, "Color", Defaults.BackdropColor),
            Text(back, "Blank_Texture_Name") ?? Defaults.BackdropTextureName,
            Names(back, "Icon_Alternate_Texture_Name") ?? Defaults.FactionFrameTextureNames,
            Style(HeaderComponent, Defaults.Header),
            Style(BodyComponent, Defaults.Body),
            Style(RightComponent, Defaults.RightText),
            Style(CenterComponent, Defaults.CenterText),
            Style(CostComponent, Defaults.CostText),
            Offsets());
    }

    /// <summary>
    ///     Where the header's pieces sit. These live on <c>GameConstants</c> rather than on the
    ///     components, so they are read from the object the same way everything else here is - a mod
    ///     that moves the blip or the portrait moves them in the preview too.
    /// </summary>
    private EncyclopediaOffsets Offsets()
    {
        var tags = TagsOf(EncyclopediaTags.GameConstantsId);
        var shipped = EncyclopediaOffsets.Shipped;

        return new EncyclopediaOffsets(
            Whole(tags, "Encyclopedia_Population_Offset", shipped.Population),
            Whole(tags, "Encyclopedia_Name_Offset", shipped.Name),
            Whole(tags, "Encyclopedia_Cost_Offset", shipped.Cost),
            Whole(tags, "Encyclopedia_Icon_X_Offset", shipped.IconX),
            Whole(tags, "Encyclopedia_Icon_Y_Offset", shipped.IconY),
            Whole(tags, "Encyclopedia_Class_Y_Offset", shipped.ClassY));
    }

    /// <summary>A whole-unit offset, falling back per tag like everything else in this resolver.</summary>
    private static int Whole(IReadOnlyDictionary<string, string> tags, string tagName, int fallback)
    {
        var value = Number(tags, tagName);
        return value is null || double.IsNaN(value.Value) ? fallback : (int)value.Value;
    }

    private IReadOnlyDictionary<string, string> TagsOf(string componentId)
    {
        var effective = _resolver.Resolve(componentId);
        if (!effective.Found)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Last occurrence wins: the shipped file restates Base_Layer within a single component, so
        // a first-wins read would pick a value the engine has already replaced.
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in effective.Tags)
            tags[tag.TagName] = tag.Value;
        return tags;
    }

    private EncyclopediaTextStyle Style(string componentId, EncyclopediaTextStyle fallback)
    {
        var tags = TagsOf(componentId);

        // Size X is the line's character budget. Truncated rather than rounded: it is a count, and
        // the engine reads it as an integer. Size Y is the row height, which the card takes from
        // the backdrop instead, so it is deliberately not read here.
        var budget = Pair(tags, "Size", double.NaN, double.NaN).X;
        var pointSize = Number(tags, "Font_Point_Size") ?? fallback.FontPointSize;
        var scale = Number(tags, "Scale") ?? fallback.Scale;
        var declaredBudget = double.IsNaN(budget) ? fallback.WrapChars : (int)budget;

        return new EncyclopediaTextStyle(
            componentId,
            Text(tags, "Font_Name") ?? fallback.FontName,
            pointSize,
            scale,
            Colour(tags, "Text_Color", fallback.TextColor),
            Alignment(tags, fallback.Alignment),
            EncyclopediaGlyphSize.WrapBudget(declaredBudget, _screenWidth, _screenHeight),
            EncyclopediaGlyphSize.Units(pointSize, scale, _screenWidth, _screenHeight));
    }

    /// <summary>
    ///     Resolves alignment from the justify tags. A component that states neither is centred, so
    ///     an explicit <c>False</c> is not the same as the tag being absent: it turns that side off
    ///     and centres the row, rather than leaving the previous default standing.
    /// </summary>
    private static string Alignment(
        IReadOnlyDictionary<string, string> tags, string fallback)
    {
        var left = Boolean(tags, "Left_Justified");
        var right = Boolean(tags, "Right_Justified");

        if (right == true) return EncyclopediaTextAlignment.Right;
        if (left == true) return EncyclopediaTextAlignment.Left;
        if (left is null && right is null) return fallback;
        return EncyclopediaTextAlignment.Center;
    }

    private static string? Text(IReadOnlyDictionary<string, string> tags, string tagName)
    {
        return tags.TryGetValue(tagName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static bool? Boolean(IReadOnlyDictionary<string, string> tags, string tagName)
    {
        var raw = Text(tags, tagName);
        return raw is null ? null : bool.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static double? Number(IReadOnlyDictionary<string, string> tags, string tagName)
    {
        var raw = Text(tags, tagName);
        return raw is not null
               && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Reads a two-number tag such as <c>Size</c> or <c>Offset</c>, falling back as a unit.</summary>
    private static (double X, double Y) Pair(
        IReadOnlyDictionary<string, string> tags, string tagName, double fallbackX, double fallbackY)
    {
        var parts = Numbers(tags, tagName);
        return parts is { Length: >= 2 } ? (parts[0], parts[1]) : (fallbackX, fallbackY);
    }

    /// <summary>
    ///     Reads a whitespace-separated list of texture names, or null when the tag is absent or
    ///     empty so the caller can fall back as a unit.
    /// </summary>
    /// <remarks>
    ///     Names pass through verbatim, case and extension included. The icon catalog already
    ///     matches either spelling - mega texture entries carry <c>.TGA</c>, loose sources are bare
    ///     base names - so normalising here would only lose what the author actually wrote.
    /// </remarks>
    private static IReadOnlyList<string>? Names(
        IReadOnlyDictionary<string, string> tags, string tagName)
    {
        var raw = Text(tags, tagName);
        if (raw is null)
            return null;

        var tokens = raw.Split([' ', '\t', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? null : tokens;
    }

    private static EncyclopediaRgba Colour(
        IReadOnlyDictionary<string, string> tags, string tagName, EncyclopediaRgba fallback)
    {
        var parts = Numbers(tags, tagName);
        return parts is { Length: >= 4 }
            ? new EncyclopediaRgba((int)parts[0], (int)parts[1], (int)parts[2], (int)parts[3])
            : fallback;
    }

    private static double[]? Numbers(IReadOnlyDictionary<string, string> tags, string tagName)
    {
        var raw = Text(tags, tagName);
        if (raw is null)
            return null;

        var tokens = raw.Split([' ', '\t', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var values = new double[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return null;
            values[i] = parsed;
        }

        return values;
    }
}