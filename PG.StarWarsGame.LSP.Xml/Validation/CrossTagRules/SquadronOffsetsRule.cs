// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

public sealed class SquadronOffsetsRule : IXmlCrossTagRule
{
    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!childrenByName.TryGetValue("Squadron_Units", out var unitNodes) || unitNodes.Count == 0)
            return [];

        var totalUnits = unitNodes
            .SelectMany(n => n.InnerText.Split(','))
            .Select(s => s.Trim())
            .Count(s => s.Length > 0);

        // Declaring no offsets at all is not a mismatch. The engine registers Squadron_Offsets as a
        // FloatVector3List at struct offset 0x820 with no named accessor, states no rule about it
        // in any of its error messages, and six of the base game's 45 squadrons omit it entirely
        // and ship that way. Reading absent as "zero offsets" made those six warn.
        //
        // Where the tag IS present the pairing holds: all 39 shipped squadrons that declare offsets
        // declare exactly one per unit.
        if (!childrenByName.TryGetValue("Squadron_Offsets", out var offsetNodes) || offsetNodes.Count == 0)
            return [];

        var totalOffsets = offsetNodes.Count;

        if (totalUnits == totalOffsets)
            return [];

        return
        [
            new SquadronOffsetsMismatchFact(
                documentUri,
                XmlUtility.GetLine(objectNode),
                XmlUtility.GetTagBracketColumn(objectNode),
                XmlUtility.GetOpeningTagLength(objectNode),
                totalUnits,
                totalOffsets,
                unitNodes.Select(ToLocation).ToList(),
                (offsetNodes ?? []).Select(ToLocation).ToList())
        ];
    }

    private static (int Line, int Column, int Length) ToLocation(HtmlNode n)
    {
        return (XmlUtility.GetLine(n), XmlUtility.GetTagBracketColumn(n), XmlUtility.GetOpeningTagLength(n));
    }
}