// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Notices an object that turns <c>Fires_Forward</c> on, and hands its id to
///     <c>FiresForwardHandler</c>.
/// </summary>
/// <remarks>
///     <para>
///         Only ON is worth a fact. The flag defaults to false in the
///         <c>GameObjectTypeByteMembersClass</c> constructor, so a written <c>No</c> changes nothing.
///     </para>
///     <para>
///         The rule stops at noticing. Whether the flag does anything depends on the object's
///         behaviours and turret extents, and those are often inherited through
///         <c>Variant_Of_Existing_Type</c> - a node-level check would misreport every variant whose base
///         carries the <c>WEAPON</c> behaviour. The handler resolves the effective object instead.
///     </para>
/// </remarks>
public sealed class FiresForwardRule : IXmlCrossTagRule
{
    private const string FiresForwardTag = "Fires_Forward";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!childrenByName.TryGetValue(FiresForwardTag, out var nodes) || nodes.Count == 0)
            return [];

        // Last occurrence wins, matching how the engine reads a repeated singleton tag.
        var node = nodes[^1];
        if (!EngineBoolean.IsTrue(node.InnerText)) return [];

        var id = XmlUtility.GetNameAttributeValue(objectNode);
        if (id is null) return []; // an unnamed object is reported elsewhere, and cannot be resolved

        var (line, column, length) = XmlUtility.GetValuePosition(node, lineIndex);
        return [new FiresForwardFact(documentUri, line, column, length, id)];
    }
}