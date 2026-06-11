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
        string documentUri)
    {
        if (!childrenByName.TryGetValue("Squadron_Units", out var unitNodes) || unitNodes.Count == 0)
            return [];

        var totalUnits = unitNodes
            .SelectMany(n => n.InnerText.Split(','))
            .Select(s => s.Trim())
            .Count(s => s.Length > 0);

        childrenByName.TryGetValue("Squadron_Offsets", out var offsetNodes);
        var totalOffsets = offsetNodes?.Count ?? 0;

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
        => (XmlUtility.GetLine(n), XmlUtility.GetTagBracketColumn(n), XmlUtility.GetOpeningTagLength(n));
}
