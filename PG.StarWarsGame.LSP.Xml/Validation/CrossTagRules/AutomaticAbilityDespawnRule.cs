// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     An ability with an automatic activation style that also causes its owner to despawn.
/// </summary>
/// <remarks>
///     <para>
///         Scoped by the two TAGS rather than by an element, deliberately.
///         <c>SpecialAbilityClass::Validate_Data</c> is the base every ability class calls first, so
///         this is every ability type's rule; naming elements here would mean listing all of them
///         and silently missing whichever one a mod reaches for.
///     </para>
///     <para>
///         The two ability classes that demand the opposite - <c>GalacticSabotageAbility</c>,
///         <c>BlackMarketAbility</c> and <c>SlicerAbility</c> all require <c>Causes_Despawn</c> to
///         be Yes - do not collide with this rule, because each also accepts only
///         <c>Ground_Activated</c>, which is not an automatic style.
///     </para>
/// </remarks>
public sealed class AutomaticAbilityDespawnRule : IXmlCrossTagRule
{
    /// <summary>
    ///     The six styles in the complaining arm of the switch in
    ///     <c>SpecialAbilityClass::Validate_Data</c>. MEASURED from that switch, not inferred from
    ///     the names: the other six cases (<c>Ground_Activated</c>, <c>Hero_Detected</c>,
    ///     <c>Combat_Imminent</c>, <c>Special_Attack</c>, <c>Take_Damage</c>, <c>User_Input</c>)
    ///     break out of it without a word.
    /// </summary>
    /// <remarks>
    ///     <c>Global_Automatic</c> is in the engine's switch and its member string is in the image,
    ///     though no shipped file uses it and our enum did not carry it until this rule needed the
    ///     complete set.
    /// </remarks>
    private static readonly HashSet<string> AutomaticStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Global_Automatic",
        "Combat_Automatic",
        "Galactic_Automatic",
        "Space_Automatic",
        "Ground_Automatic",
        "Skirmish_Automatic"
    };

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!childrenByName.TryGetValue("Activation_Style", out var styleNodes)) return [];
        if (!childrenByName.TryGetValue("Causes_Despawn", out var despawnNodes)) return [];

        // An absent or denied flag is the ordinary case and the engine says nothing about it.
        if (!despawnNodes.Any(n => EngineBoolean.IsTrue(n.InnerText.Trim()))) return [];

        var style = styleNodes
            .Select(n => n.InnerText.Trim())
            .FirstOrDefault(v => AutomaticStyles.Contains(v));

        if (style is null) return [];

        // The engine's correction is CausesDespawn = false, applied to the flag that is on. Where
        // several occurrences are on, the last one is what the parser keeps, so that is the one to
        // rewrite.
        var offending = despawnNodes.LastOrDefault(n => EngineBoolean.IsTrue(n.InnerText.Trim()));

        return
        [
            new AutomaticDespawnFact(
                documentUri,
                XmlUtility.GetLine(objectNode),
                XmlUtility.GetTagBracketColumn(objectNode),
                XmlUtility.GetOpeningTagLength(objectNode),
                style,
                ValueEditFactory.ReplaceValue(offending, lineIndex, "No"))
        ];
    }
}