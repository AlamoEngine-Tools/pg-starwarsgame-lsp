// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Flags a <c>HardPoint</c> that is declared destroyable but has neither an
///     <c>Attachment_Bone</c> nor a <c>Fire_Bone_A</c>. With no bone the engine cannot give it a
///     world position, so it can never be targeted and is indestructible in practice - the unit
///     keeps a weak point that can never be shot off, contradicting its own
///     <c>Is_Destroyable</c> (#53).
///     <para>
///         Either bone will do, which is the engine's own condition:
///         <c>HardPointClass::Get_Transformed_World_Position</c> (<c>009c92a0</c>) asserts
///         <c>AttachmentBoneIndex &gt;= 0 || FireBoneAIndex &gt;= 0</c> and falls back to the fire
///         bone. Destruction reads no bone at all - <c>HardPointClass::Take_Damage</c>
///         (<c>009c8510</c>) consults only <c>Is_Destroyable()</c> and health - so the failure is
///         about being targetable, not about destruction.
///     </para>
///     <para>
///         The <c>Is_Destroyable</c> condition is not incidental. A hardpoint that is deliberately not
///         destroyable has nothing to attach and legitimately omits the bone: across vanilla EaW and FoC
///         every one of the 133 hardpoints without an <c>Attachment_Bone</c> is explicitly
///         <c>Is_Destroyable&gt;No</c>, and no destroyable hardpoint is missing one. Dropping the
///         condition would turn a quarter of the shipped hardpoints into errors.
///     </para>
///     <para>
///         Document-local on purpose: this needs no model data and no reverse lookup, so it is the one
///         hardpoint check that is always decidable. Whether a declared bone actually exists on the
///         referencing units' models is a separate, model-data-dependent check.
///     </para>
/// </summary>
public sealed class HardpointAttachmentBoneRule : IXmlCrossTagRule
{
    private const string HardpointElement = "hardpoint";
    private const string AttachmentBoneTag = "Attachment_Bone";

    /// <summary>Only FireBoneA is named in the engine's assert; FireBoneB is not a fallback.</summary>
    private const string FireBoneTag = "Fire_Bone_A";

    private const string IsDestroyableTag = "Is_Destroyable";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        // HAP lowercases element names.
        if (!objectNode.Name.Equals(HardpointElement, StringComparison.OrdinalIgnoreCase))
            return [];

        // Either bone gives the hardpoint a world position, and that is what the engine actually
        // requires: HardPointClass::Get_Transformed_World_Position (009c92a0) asserts
        // "AttachmentBoneIndex >= 0 || FireBoneAIndex >= 0" and falls back to the fire bone when the
        // attachment bone is absent. Its error (014d6160) fires only when BOTH are -1.
        //
        // Destruction itself reads no bone - HardPointClass::Take_Damage (009c8510) checks
        // Is_Destroyable() and health and nothing else. A boneless hardpoint is indestructible
        // because it cannot be positioned and so cannot be targeted, which a fire bone also fixes.
        if (HasValue(childrenByName, AttachmentBoneTag) || HasValue(childrenByName, FireBoneTag))
            return [];

        // Only a hardpoint that claims to be destroyable is contradicting itself. Vanilla always
        // states Is_Destroyable explicitly; when it is absent the engine default is unknown, so stay
        // silent rather than guess and risk flagging correct data.
        if (!IsExplicitlyDestroyable(childrenByName))
            return [];

        var id = XmlUtility.GetNameAttributeValue(objectNode);
        if (id is null)
            return []; // an unnamed hardpoint is a different problem, reported elsewhere

        return
        [
            new HardpointMissingAttachmentBoneFact(
                documentUri,
                XmlUtility.GetLine(objectNode),
                XmlUtility.GetTagBracketColumn(objectNode),
                XmlUtility.GetOpeningTagLength(objectNode),
                id)
        ];
    }

    private static bool HasValue(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName, string tag)
    {
        return childrenByName.TryGetValue(tag, out var nodes)
               && nodes.Any(n => n.InnerText.Trim().Length > 0);
    }

    private static bool IsExplicitlyDestroyable(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName)
    {
        if (!childrenByName.TryGetValue(IsDestroyableTag, out var nodes))
            return false;

        // Last occurrence wins, matching how the engine reads duplicated tags.
        return EngineBoolean.IsTrue(
            nodes.LastOrDefault(n => n.InnerText.Trim().Length > 0)?.InnerText);
    }
}
