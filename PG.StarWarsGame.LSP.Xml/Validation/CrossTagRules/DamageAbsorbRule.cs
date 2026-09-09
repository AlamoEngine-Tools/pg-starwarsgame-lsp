// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Reports an absorb configuration that resolves to nothing - both terms of
///     <c>(damage * percentage) + amount</c> at zero.
/// </summary>
/// <remarks>
///     Needs both tags at once, so it is a cross-tag rule rather than a value check: neither value
///     is wrong on its own, and zero is a legitimate setting for either one alone.
/// </remarks>
public sealed class DamageAbsorbRule : IXmlCrossTagRule
{
    private const string PercentageTag = "Damage_Absorb_Percentage";
    private const string AmountTag = "Damage_Absorb_Amount";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        var percentageNode = Last(childrenByName, PercentageTag);
        var amountNode = Last(childrenByName, AmountTag);

        // An object that never mentions absorb is not configuring it.
        if (percentageNode is null && amountNode is null) return [];

        // A value that is not a number is the Float check's business; reporting it here too would
        // put two diagnostics on one typo, and there is no absorb total to judge either way.
        if (!TryValue(percentageNode, out var percentage)) return [];
        if (!TryValue(amountNode, out var amount)) return [];

        if ((percentage ?? 0) != 0 || (amount ?? 0) != 0) return [];

        // Anchored on whichever tag is present - the one the author can see and edit. Preferring
        // the percentage keeps the report in a stable place when both are there.
        var anchor = percentageNode ?? amountNode!;
        return
        [
            new DamageAbsorbsNothingFact(
                documentUri,
                XmlUtility.GetLine(anchor),
                XmlUtility.GetTagBracketColumn(anchor),
                XmlUtility.GetOpeningTagLength(anchor),
                percentage,
                amount)
        ];
    }

    /// <summary>The last occurrence, matching the engine's keep-the-last rule for repeated tags.</summary>
    private static HtmlNode? Last(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName, string tag)
    {
        return childrenByName.TryGetValue(tag, out var nodes) && nodes.Count > 0
            ? nodes[^1]
            : null;
    }

    /// <summary>Absent reads as null (the formula treats a missing term as zero); unparseable fails.</summary>
    private static bool TryValue(HtmlNode? node, out double? value)
    {
        value = null;
        if (node is null) return true;

        var text = node.InnerText.Trim();
        if (text.Length == 0) return true;

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = parsed;
        return true;
    }
}
