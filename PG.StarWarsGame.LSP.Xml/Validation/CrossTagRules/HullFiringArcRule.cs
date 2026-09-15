// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Notices an object that writes a turret-extent tag, and hands its id to <c>HullFiringArcHandler</c>
///     (A5).
/// </summary>
/// <remarks>
///     The rule stops at noticing: whether the tags are the hull's arc depends on the object's behaviours,
///     <c>Fires_Forward</c> and <c>Turret_XY_Only</c>, all routinely inherited. Anchored on the first of the
///     four tags this object writes, in the order a reader looks for them.
/// </remarks>
public sealed class HullFiringArcRule : IXmlCrossTagRule
{
    private static readonly string[] ExtentTags =
    [
        "Turret_Rotate_Extent_Degrees",
        "Turret_Elevate_Extent_Degrees",
        "Deployed_Turret_Rotate_Extent_Degrees",
        "Deployed_Turret_Elevate_Extent_Degrees"
    ];

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        var anchor = ExtentTags
            .Select(tag => childrenByName.TryGetValue(tag, out var nodes)
                ? nodes.LastOrDefault(n => n.InnerText.Trim().Length > 0)
                : null)
            .FirstOrDefault(node => node is not null);
        if (anchor is null) return [];

        var id = XmlUtility.GetNameAttributeValue(objectNode);
        if (id is null) return []; // an unnamed object is reported elsewhere, and cannot be resolved

        var (line, column, length) = XmlUtility.GetValuePosition(anchor, lineIndex);
        return [new HullFiringArcFact(documentUri, line, column, length, id)];
    }
}