// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Flags a <c>HardPoint</c> that is declared destroyable but has no <c>Attachment_Bone</c>. Without
///     one the engine cannot attach it to the parent model and it becomes indestructible - so the unit
///     keeps a weak point that can never be shot off, contradicting its own <c>Is_Destroyable</c> (#53).
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
    private const string IsDestroyableTag = "Is_Destroyable";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri)
    {
        // HAP lowercases element names.
        if (!objectNode.Name.Equals(HardpointElement, StringComparison.OrdinalIgnoreCase))
            return [];

        if (childrenByName.TryGetValue(AttachmentBoneTag, out var bones)
            && bones.Any(b => b.InnerText.Trim().Length > 0))
            return [];

        // Only a hardpoint that claims to be destroyable is contradicting itself. Vanilla always
        // states Is_Destroyable explicitly; when it is absent the engine default is unknown, so stay
        // silent rather than guess and risk flagging correct data.
        if (!IsExplicitlyDestroyable(childrenByName))
            return [];

        var id = NameAttribute(objectNode);
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

    private static bool IsExplicitlyDestroyable(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName)
    {
        if (!childrenByName.TryGetValue(IsDestroyableTag, out var nodes))
            return false;

        // Last occurrence wins, matching how the engine reads duplicated tags.
        var value = nodes.LastOrDefault(n => n.InnerText.Trim().Length > 0)?.InnerText.Trim();
        return value is not null
               && (value.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("True", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("1", StringComparison.Ordinal));
    }

    private static string? NameAttribute(HtmlNode node)
    {
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals("Name", StringComparison.OrdinalIgnoreCase));
        var value = attr?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
