// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Base for the engine's "if you set A to true you must also set B" rules.
/// </summary>
/// <remarks>
///     <para>
///         Scoped to the ELEMENT, unlike <see cref="TagComparisonRuleBase" />, which gets its
///         scoping free from needing both tags present. That trick is unavailable here: most of the
///         value of this rule is firing when the required tag is ABSENT, so presence cannot decide
///         whether the rule applies.
///     </para>
///     <para>
///         Anchored on the GATE, because that is the tag the author turned on and the one they will
///         look at first. The alternative - anchoring on a tag that is not in the file - has nowhere
///         to put the marker.
///     </para>
/// </remarks>
public abstract class BooleanGatedRequirementRuleBase : IXmlCrossTagRule
{
    /// <summary>The owning element, lower-cased and underscored as HAP reports it.</summary>
    protected abstract string ElementName { get; }

    /// <summary>The flag whose being on creates the requirement.</summary>
    protected abstract string GateTag { get; }

    /// <summary>The tag that must then be set.</summary>
    protected abstract string RequiredTag { get; }

    /// <summary>What the required tag must be, completing "&lt;Required&gt; must be ...".</summary>
    protected virtual string Requirement => "set to true";

    /// <summary>What the engine silently does instead when the requirement is unmet.</summary>
    protected virtual string Repair => "turns it on for you";

    /// <summary>
    ///     Whether the required tag's value satisfies the engine. Absent is never satisfied, so
    ///     this is only asked about a value that is actually present.
    /// </summary>
    protected virtual bool IsSatisfied(string value)
    {
        return EngineBoolean.IsTrue(value);
    }

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!string.Equals(objectNode.Name, ElementName, StringComparison.OrdinalIgnoreCase))
            return [];

        var gateNode = Last(childrenByName, GateTag);
        if (gateNode is null || !EngineBoolean.IsTrue(gateNode.InnerText.Trim()))
            return [];

        var requiredNode = Last(childrenByName, RequiredTag);
        if (requiredNode is not null && IsSatisfied(requiredNode.InnerText.Trim()))
            return [];

        return
        [
            new BooleanGatedRequirementFact(
                documentUri,
                XmlUtility.GetLine(gateNode),
                XmlUtility.GetTagBracketColumn(gateNode),
                XmlUtility.GetOpeningTagLength(gateNode),
                GateTag,
                RequiredTag,
                Requirement,
                Repair)
        ];
    }

    /// <summary>The last occurrence, matching the engine's keep-the-last rule for repeated tags.</summary>
    private static HtmlNode? Last(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName, string tag)
    {
        return childrenByName.TryGetValue(tag, out var nodes) && nodes.Count > 0 ? nodes[^1] : null;
    }

    /// <summary>Parses a value the way the numeric-gated subclass needs it.</summary>
    protected static bool IsPositiveNumber(string value)
    {
        return double.TryParse(value.TrimEnd('f', 'F'), NumberStyles.Float,
                   CultureInfo.InvariantCulture, out var parsed)
               && parsed > 0.0;
    }
}
