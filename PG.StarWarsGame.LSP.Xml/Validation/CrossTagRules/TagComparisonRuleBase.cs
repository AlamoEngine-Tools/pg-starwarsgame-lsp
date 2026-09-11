// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Base for the rules where the engine relates one numeric tag to another.
/// </summary>
/// <remarks>
///     <para>
///         Neither value is wrong on its own, so this cannot be a value check - it needs the
///         object's whole child set, which is what a cross-tag rule gets.
///     </para>
///     <para>
///         Scoping comes free from requiring BOTH tags: <c>Damage_Radius</c> also appears on
///         <c>ShieldFlareAbility</c>, which has no <c>Chase_Radius</c>, so the rule is silent there
///         without needing to name the element.
///     </para>
/// </remarks>
public abstract class TagComparisonRuleBase : IXmlCrossTagRule
{
    /// <summary>The tag the report is anchored on - the one the author is most likely editing.</summary>
    protected abstract string LeftTag { get; }

    /// <summary>The tag it is compared against.</summary>
    protected abstract string RightTag { get; }

    /// <summary>The relation as a sentence fragment completing "&lt;Left&gt; ... &lt;Right&gt;".</summary>
    protected abstract string Expectation { get; }

    /// <summary>Whether the pair is acceptable.</summary>
    protected abstract bool IsAcceptable(double left, double right);

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        var leftNode = Last(childrenByName, LeftTag);
        var rightNode = Last(childrenByName, RightTag);

        // A relation needs both sides. One tag alone says nothing about the other.
        if (leftNode is null || rightNode is null) return [];

        // A value that is not a number is the type check's business; there is no relation to judge
        // either way, and one typo should not collect two diagnostics.
        if (!TryValue(leftNode, out var left) || !TryValue(rightNode, out var right)) return [];

        if (IsAcceptable(left, right)) return [];

        return
        [
            new TagComparisonFact(
                documentUri,
                XmlUtility.GetLine(leftNode),
                XmlUtility.GetTagBracketColumn(leftNode),
                XmlUtility.GetOpeningTagLength(leftNode),
                LeftTag,
                RightTag,
                left,
                right,
                Expectation)
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

    private static bool TryValue(HtmlNode node, out double value)
    {
        return double.TryParse(
            node.InnerText.Trim().TrimEnd('f', 'F'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }
}
