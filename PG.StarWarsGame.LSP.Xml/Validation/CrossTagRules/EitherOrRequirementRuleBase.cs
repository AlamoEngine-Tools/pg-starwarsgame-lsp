// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Reports an object where neither half of an either/or pair is set, so it does nothing.
/// </summary>
/// <remarks>
///     <para>
///         The engine's own test in every case is "neither" - either tag on its own satisfies it -
///         and the engine repairs none of them. The object loads, the ability runs, and it has no
///         effect.
///     </para>
///     <para>
///         A rule names a SET of elements because the engine does: one message string is referenced
///         from up to four ability classes, and the
///         <c>Applicable_Unit_Categories</c>/<c>Applicable_Unit_Types</c> check alone is stated in
///         nine. Modelling that as nine near-identical rule classes would have made the shape harder
///         to see, not easier.
///     </para>
///     <para>
///         What counts as "set" varies with the tags: a flag is set when it reads true, a list when
///         it has content. <see cref="IsSatisfied" /> is the seam, and the default is the boolean
///         reading the first three rules were written against.
///     </para>
/// </remarks>
public abstract class EitherOrRequirementRuleBase : IXmlCrossTagRule
{
    /// <summary>The elements this rule covers, as HAP reports them - lower-cased and underscored.</summary>
    protected abstract IReadOnlyList<string> ElementNames { get; }

    /// <summary>One half of the pair, in the order the engine's own message names them.</summary>
    protected abstract string FirstTag { get; }

    /// <summary>The other half.</summary>
    protected abstract string SecondTag { get; }

    /// <summary>How the message describes both halves being unset - "off" for flags.</summary>
    protected virtual string State => "off";

    /// <summary>What the object cannot do, in the engine's own words where it gives them.</summary>
    protected virtual string Consequence => "does nothing";

    /// <summary>What the author should do about it.</summary>
    protected virtual string Remedy => "set one of them to Yes";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!ElementNames.Contains(objectNode.Name, StringComparer.OrdinalIgnoreCase)) return [];

        if (!Applies(childrenByName)) return [];

        if (IsSet(childrenByName, FirstTag) || IsSet(childrenByName, SecondTag)) return [];

        return
        [
            new EitherOrRequirementFact(
                documentUri,
                XmlUtility.GetLine(objectNode),
                XmlUtility.GetTagBracketColumn(objectNode),
                XmlUtility.GetOpeningTagLength(objectNode),
                OwningTypeOf(objectNode),
                FirstTag,
                SecondTag,
                State,
                Consequence,
                Remedy)
        ];
    }

    /// <summary>
    ///     Whether a written value counts as set. A flag must read true; a list only has to carry
    ///     something, which is why a list rule must not inherit the boolean reading - a category
    ///     legitimately named "No" is content, and the engine counts content.
    /// </summary>
    protected virtual bool IsSatisfied(string value)
    {
        return EngineBoolean.IsTrue(value);
    }

    /// <summary>
    ///     Whether the engine asks the question of this object at all.
    /// </summary>
    /// <remarks>
    ///     Nearly always yes. <c>ForceHealingAbilityClass</c> is the exception: it only asks which
    ///     units it applies to when <c>Heal_Range</c> is positive, so an ability that heals at no
    ///     range is not required to name any. Reporting it would be inventing a rule the engine
    ///     does not state for that object.
    /// </remarks>
    protected virtual bool Applies(IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName)
    {
        return true;
    }

    /// <summary>
    ///     The schema type name for the message, derived from the element the rule actually matched -
    ///     a rule covering nine owners cannot carry one owner's name.
    /// </summary>
    private static string OwningTypeOf(HtmlNode objectNode)
    {
        return XmlUtility.ToPascalCase(objectNode.Name);
    }

    /// <summary>
    ///     A repeated tag counts when ANY occurrence counts, matching the engine: the last write wins
    ///     for a scalar, but reporting "neither is set" over a file that plainly sets one somewhere
    ///     would be wrong whichever occurrence the engine kept.
    /// </summary>
    private bool IsSet(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName, string tag)
    {
        return childrenByName.TryGetValue(tag, out var nodes)
               && nodes.Any(n => IsSatisfied(n.InnerText.Trim()));
    }
}