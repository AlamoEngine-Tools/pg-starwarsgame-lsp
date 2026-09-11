// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Reports a <c>Leech_Shields_Ability</c> that omits a tag the engine refuses to run without.
/// </summary>
/// <remarks>
///     <para>
///         Seven tags, each with its own message in the binary - <c>Error: (%s) Beam_Bone_Name has
///         not been set.</c> (<c>0155803c</c>), and the same for <c>Beam_Effect_Name</c>,
///         <c>Beam_Texture_Name</c>, <c>Beam_Frames</c>, <c>Beam_Width</c>,
///         <c>Damage_Multiplier</c> and <c>Shield_Damage_Per_Second</c>.
///     </para>
///     <para>
///         Scoped to the element, not the tag, and that matters: <c>Beam_Texture_Name</c> also
///         appears on <c>Super_Laser_Ability</c> (the Eclipse) and <c>Beam_Frames</c> and
///         <c>Beam_Width</c> on <c>Energy_Weapon_Attack_Ability</c>, where these messages do not
///         apply. A tag-scoped rule would fire on units that are perfectly well configured.
///     </para>
///     <para>
///         Corroborated by the one shipped instance: the Kadalbe battleship's
///         <c>Kadalbe_Leech_Shields</c> sets all seven, so the base game agrees with the engine and
///         this rule is silent on vanilla data.
///     </para>
/// </remarks>
public sealed class LeechShieldsRequiredTagsRule : IXmlCrossTagRule
{
    private const string OwningType = "LeechShieldsAbility";

    private static readonly string[] Required =
    [
        "Beam_Bone_Name",
        "Beam_Effect_Name",
        "Beam_Frames",
        "Beam_Texture_Name",
        "Beam_Width",
        "Damage_Multiplier",
        "Shield_Damage_Per_Second",
    ];

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!IsLeechShields(objectNode)) return [];

        var facts = new List<XmlFact>();
        foreach (var tag in Required)
        {
            // Present but blank is still unset as far as the engine is concerned.
            if (childrenByName.TryGetValue(tag, out var nodes)
                && nodes.Any(n => !string.IsNullOrWhiteSpace(n.InnerText)))
                continue;

            facts.Add(new MissingRequiredTagFact(
                documentUri,
                XmlUtility.GetLine(objectNode),
                XmlUtility.GetTagBracketColumn(objectNode),
                XmlUtility.GetOpeningTagLength(objectNode),
                OwningType,
                tag));
        }

        return facts;
    }

    /// <summary>HAP lower-cases element names, so compare on the underscored form directly.</summary>
    private static bool IsLeechShields(HtmlNode node)
    {
        return string.Equals(node.Name, "leech_shields_ability", StringComparison.OrdinalIgnoreCase);
    }
}
