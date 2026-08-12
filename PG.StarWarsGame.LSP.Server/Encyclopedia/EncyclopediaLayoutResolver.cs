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
    public static readonly EncyclopediaLayout Defaults = new(
        262d, 14d, 5d, 2d, 0.75d,
        new EncyclopediaRgba(255, 255, 255, 128),
        new EncyclopediaTextStyle(HeaderComponent, "Arial Bold", 7d, 1.0d,
            new EncyclopediaRgba(255, 255, 255, 255), EncyclopediaTextAlignment.Left),
        new EncyclopediaTextStyle(BodyComponent, "Arial", 7d, 1.0d,
            new EncyclopediaRgba(192, 192, 192, 200), EncyclopediaTextAlignment.Left),
        new EncyclopediaTextStyle(RightComponent, "Arial", 7d, 1.0d,
            new EncyclopediaRgba(192, 192, 192, 255), EncyclopediaTextAlignment.Right),
        new EncyclopediaTextStyle(CenterComponent, "Arial", 7d, 1.0d,
            new EncyclopediaRgba(255, 255, 255, 255), EncyclopediaTextAlignment.Center),
        new EncyclopediaTextStyle(CostComponent, "EmpireAtWar-Bold", 7d, 1.0d,
            new EncyclopediaRgba(192, 192, 192, 255), EncyclopediaTextAlignment.Right));

    private readonly EffectiveObjectResolver _resolver;

    public EncyclopediaLayoutResolver(EffectiveObjectResolver resolver)
    {
        _resolver = resolver;
    }

    public EncyclopediaLayout Resolve()
    {
        var back = TagsOf(BackComponent);
        var (width, rowHeight) = Pair(back, "Size", Defaults.Width, Defaults.RowHeight);
        var (offsetX, offsetY) = Pair(back, "Offset", Defaults.OffsetX, Defaults.OffsetY);

        // encyclopedia_icon's Size is a pair of scales, not a pixel size: X scales the unit icon in
        // the header, Y the ability icons. Falls back as a unit with a dummy Y, which is unused.
        var (iconScale, _) = Pair(TagsOf(IconComponent), "Size", Defaults.IconScale, 0d);

        return new EncyclopediaLayout(
            width,
            rowHeight,
            offsetX,
            offsetY,
            iconScale,
            Colour(back, "Color", Defaults.BackdropColor),
            Style(HeaderComponent, Defaults.Header),
            Style(BodyComponent, Defaults.Body),
            Style(RightComponent, Defaults.RightText),
            Style(CenterComponent, Defaults.CenterText),
            Style(CostComponent, Defaults.CostText));
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

        return new EncyclopediaTextStyle(
            componentId,
            Text(tags, "Font_Name") ?? fallback.FontName,
            Number(tags, "Font_Point_Size") ?? fallback.FontPointSize,
            Number(tags, "Scale") ?? fallback.Scale,
            Colour(tags, "Text_Color", fallback.TextColor),
            Alignment(tags, fallback.Alignment));
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
