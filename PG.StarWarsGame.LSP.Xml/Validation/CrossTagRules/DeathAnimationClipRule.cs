// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Notices an object that writes <c>Specific_Death_Anim_Type</c>, and hands its id to
///     <c>DeathAnimationClipHandler</c> (#104).
/// </summary>
/// <remarks>
///     The rule stops at noticing: whether the model has the clip depends on the model, the index and
///     <c>Remove_Upon_Death</c>, all routinely inherited.
/// </remarks>
public sealed class DeathAnimationClipRule : IXmlCrossTagRule
{
    private const string TypeTag = "Specific_Death_Anim_Type";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!childrenByName.TryGetValue(TypeTag, out var nodes)) return [];

        var anchor = nodes.LastOrDefault(n => n.InnerText.Trim().Length > 0);
        if (anchor is null) return [];

        var id = XmlUtility.GetNameAttributeValue(objectNode);
        if (id is null) return []; // an unnamed object is reported elsewhere, and cannot be resolved

        var (line, column, length) = XmlUtility.GetValuePosition(anchor, lineIndex);
        return [new DeathAnimationClipFact(documentUri, line, column, length, id)];
    }
}
