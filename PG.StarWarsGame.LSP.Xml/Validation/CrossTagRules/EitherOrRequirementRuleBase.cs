// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Reports an ability with both halves of an either/or flag pair switched off.
/// </summary>
/// <remarks>
///     <para>
///         The engine's own test is both-off, and an absent boolean parses as false, so an ability
///         that writes neither flag fires this as surely as one that writes two explicit Nos.
///     </para>
///     <para>
///         Read by MEANING rather than by spelling - <c>Yes</c>, <c>True</c> and <c>1</c> are
///         interchangeable to the engine. An unrecognised spelling reads as false here exactly as
///         it does there; the typo itself belongs to the boolean value handler, and one mistake
///         should not collect two diagnostics saying different things.
///     </para>
/// </remarks>
public abstract class EitherOrRequirementRuleBase : IXmlCrossTagRule
{
    /// <summary>The element as HAP reports it - lower-cased and underscored.</summary>
    protected abstract string ElementName { get; }

    /// <summary>The schema type name, for the message.</summary>
    protected abstract string OwningType { get; }

    /// <summary>One half of the pair, in the order the engine's own message names them.</summary>
    protected abstract string FirstTag { get; }

    /// <summary>The other half.</summary>
    protected abstract string SecondTag { get; }

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!string.Equals(objectNode.Name, ElementName, StringComparison.OrdinalIgnoreCase))
            return [];

        if (IsOn(childrenByName, FirstTag) || IsOn(childrenByName, SecondTag))
            return [];

        return
        [
            new EitherOrRequirementFact(
                documentUri,
                XmlUtility.GetLine(objectNode),
                XmlUtility.GetTagBracketColumn(objectNode),
                XmlUtility.GetOpeningTagLength(objectNode),
                OwningType,
                FirstTag,
                SecondTag)
        ];
    }

    /// <summary>
    ///     A repeated tag is on when ANY occurrence is on, matching the engine: the last write wins
    ///     for a scalar, but reporting "both off" over a file that plainly says Yes somewhere would
    ///     be wrong whichever occurrence the engine kept.
    /// </summary>
    private static bool IsOn(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName, string tag)
    {
        return childrenByName.TryGetValue(tag, out var nodes)
               && nodes.Any(n => EngineBoolean.IsTrue(n.InnerText.Trim()));
    }
}
