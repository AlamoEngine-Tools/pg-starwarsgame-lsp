// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Notices an object that writes either side of the #101 comparison - its
///     <c>Targeting_Max_Attack_Distance</c> or its <c>HardPoints</c> list - and hands its id to
///     <c>AttackDistanceBeyondHardpointRangeHandler</c>.
/// </summary>
/// <remarks>
///     <para>
///         The rule stops at noticing. Both sides are routinely inherited, and the hardpoints' ranges live
///         on other objects entirely, so the comparison is the handler's, against effective objects.
///     </para>
///     <para>
///         Anchored on the attack distance's value when this object writes one, because that is the value
///         a quick fix replaces. Otherwise on the <c>HardPoints</c> value, so a variant that only swaps its
///         hardpoints is still checked - but a fix there would overwrite the list, so the fact says which.
///     </para>
/// </remarks>
public sealed class AttackDistanceRule : IXmlCrossTagRule
{
    private const string AttackDistanceTag = "Targeting_Max_Attack_Distance";
    private const string HardpointsTag = "HardPoints";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        var onDistance = LastWithValue(childrenByName, AttackDistanceTag);
        var anchor = onDistance ?? LastWithValue(childrenByName, HardpointsTag);
        if (anchor is null) return [];

        var id = XmlUtility.GetNameAttributeValue(objectNode);
        if (id is null) return []; // an unnamed object is reported elsewhere, and cannot be resolved

        var (line, column, length) = XmlUtility.GetValuePosition(anchor, lineIndex);
        return [new AttackDistanceFact(documentUri, line, column, length, id, onDistance is not null)];
    }

    /// <summary>The last occurrence with a value, matching how the engine reads a repeated tag.</summary>
    private static HtmlNode? LastWithValue(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName, string tag)
    {
        return childrenByName.TryGetValue(tag, out var nodes)
            ? nodes.LastOrDefault(n => n.InnerText.Trim().Length > 0)
            : null;
    }
}
