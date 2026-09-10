// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     The <c>Land_Damage_*</c> table (#102): <c>Land_Damage_Thresholds</c> and
///     <c>Land_Damage_Alternates</c> pair by position, so they must carry the same number of entries.
/// </summary>
/// <remarks>
///     <para>
///         The issue asks for all THREE columns to agree, and the shipped corpus refuses that rule.
///         Measured with XML comments stripped, <c>Land_Damage_SFX</c> disagrees with the alternates
///         on 42 of foc's 219 objects and 37 of eaw's 161, in two shapes: 35 objects pair a single
///         alternate with four <c>null</c> sounds, and 7 pair three alternates with four entries
///         whose middle two are real. Enforcing it would fire on the base game, so the SFX column is
///         not read here at all. The threshold/alternate pairing disagrees in ZERO objects of either
///         corpus, which is what makes it safe to report - and it is the pairing the preview's own
///         damage table already relies on.
///     </para>
///     <para>
///         Silent unless BOTH columns are present on the same object. This rule sees the document
///         node rather than the effective object, so a variant that overrides one column and
///         inherits the other is indistinguishable from an object that declares one alone - and
///         "cannot tell" is not "fails". Nothing in either corpus declares one of the two by itself,
///         so nothing measurable is lost.
///     </para>
/// </remarks>
public sealed class LandDamageTableRule : IXmlCrossTagRule
{
    private const string ThresholdsTag = "Land_Damage_Thresholds";
    private const string AlternatesTag = "Land_Damage_Alternates";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!TryColumn(childrenByName, ThresholdsTag, out var thresholdNode, out var thresholds) ||
            !TryColumn(childrenByName, AlternatesTag, out var alternateNode, out var alternates))
            return [];

        if (thresholds.Count == alternates.Count)
            return [];

        return
        [
            new LandDamageTableMismatchFact(
                documentUri,
                XmlUtility.GetLine(objectNode),
                XmlUtility.GetTagBracketColumn(objectNode),
                XmlUtility.GetOpeningTagLength(objectNode),
                thresholds,
                alternates,
                XmlUtility.GetValuePosition(thresholdNode, lineIndex),
                XmlUtility.GetValuePosition(alternateNode, lineIndex))
        ];
    }

    /// <summary>
    ///     The entries of one column, or false when the object does not declare it.
    /// </summary>
    /// <remarks>
    ///     The LAST occurrence wins, matching what the game does with a repeated singleton tag; an
    ///     earlier one is <c>XmlDuplicateTagHandler</c>'s business, not this rule's. An empty slot -
    ///     a trailing or doubled comma - is not an entry, because the table pairs by position and
    ///     there is nothing at that position to pair.
    /// </remarks>
    private static bool TryColumn(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string tag,
        out HtmlNode node,
        out IReadOnlyList<string> entries)
    {
        node = null!;
        entries = [];

        if (!childrenByName.TryGetValue(tag, out var nodes) || nodes.Count == 0)
            return false;

        node = nodes[^1];
        entries = node.InnerText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return true;
    }
}
