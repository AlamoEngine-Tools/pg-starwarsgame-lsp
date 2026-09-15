// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     A system spy's <c>Duration_In_Secs</c> against the <c>Activation_Style</c> it declares.
/// </summary>
/// <remarks>
///     <para>
///         <c>SystemSpyAbilityClass::Validate_Data</c> (<c>0101dcdf</c>) demands the opposite sign
///         in each arm: <c>Galactic_Automatic</c> complains when <c>0.0 &lt;= duration</c> and
///         repairs to <c>-1.0</c>, <c>Ground_Activated</c> complains when <c>duration &lt;= 0.0</c>
///         and repairs to <c>30.0</c>. Zero is refused by both, which the comparisons say and the
///         message's parentheticals happen to agree with.
///     </para>
///     <para>
///         Measured on the shipped corpus: 46 <c>System_Spy_Ability</c> declarations across
///         <c>eaw/</c> and <c>foc/</c>, all of them either <c>Galactic_Automatic</c> at
///         <c>-1.0</c> or <c>Ground_Activated</c> at a positive value. Zero violations, and no
///         third style anywhere - which is what the <c>allowedValues</c> on that owner's
///         <c>Activation_Style</c> now states.
///     </para>
///     <para>
///         Element-scoped rather than tag-scoped, unlike <see cref="AutomaticAbilityDespawnRule" />:
///         this one is stated by the leaf class, not by the ability base, so it is only true of
///         this element.
///     </para>
/// </remarks>
public sealed class SystemSpyDurationRule : IXmlCrossTagRule
{
    private const string ElementName = "system_spy_ability";
    private const string StyleTag = "Activation_Style";
    private const string DurationTag = "Duration_In_Secs";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!string.Equals(objectNode.Name, ElementName, StringComparison.OrdinalIgnoreCase))
            return [];

        // The last occurrence of each, matching the engine's keep-the-last rule for repeated tags.
        if (Last(childrenByName, StyleTag) is not { } styleNode) return [];
        if (Last(childrenByName, DurationTag) is not { } durationNode) return [];

        var style = styleNode.InnerText.Trim();
        var raw = durationNode.InnerText.Trim();

        // A duration that is not a number never reaches the comparison; the type handler reports it.
        if (!double.TryParse(raw.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var duration))
            return [];

        var (requirement, repair) = style.ToLowerInvariant() switch
        {
            "galactic_automatic" when duration >= 0.0 => ("below 0", "-1.0"),
            "ground_activated" when duration <= 0.0 => ("above 0", "30.0"),
            // Either the value is fine, or the style is one the class refuses outright - and that
            // third case belongs to the allowed-values check on the tag, which reports it where it
            // is written. Choosing a bound for a style the engine does not handle would invent one.
            _ => (null, null)
        };

        if (requirement is null || repair is null) return [];

        var (line, column, length) = XmlUtility.GetValuePosition(durationNode, lineIndex);

        return [new SystemSpyDurationFact(documentUri, line, column, length, style, duration, requirement, repair)];
    }

    private static HtmlNode? Last(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> children, string tag)
    {
        return children.TryGetValue(tag, out var nodes) && nodes.Count > 0 ? nodes[^1] : null;
    }
}