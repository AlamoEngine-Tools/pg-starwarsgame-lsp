// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     A cloaking unit against the parts of its model the cloak decides by name, and against the
///     models its hardpoints attach.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the game: a cloak hides every part whose name contains BASEMESH and shows every
///         part whose name contains STEALTH; a stealth also hides the models a hardpoint attaches, a
///         Force cloak does not.
///     </para>
///     <para>
///         On the unit's XML object, never on the model: a model shared by a cloaking unit and one
///         that cannot cloak is only in question for the first. The model is found the way every
///         model-backed rule finds it (<see cref="HardpointBoneModelResolver.DeclaredModels" />) and its
///         part names come from the catalogue: bone and mesh names, and the names its proxies carry.
///         Bone names are in that set too, so a bone with BASEMESH or STEALTH in its name and no
///         part beside it silences the rule.
///     </para>
/// </remarks>
public sealed class XmlCloakFactProducer(ISchemaProvider schema, IVariantTagSource tagSource) : IXmlFactProducer
{
    private const string BaseMeshMark = "BASEMESH";
    private const string StealthMark = "STEALTH";
    private const string AbilitiesTag = "Abilities";
    private const string HardPointsTag = "HardPoints";
    private const string ModelToAttachTag = "Model_To_Attach";

    public string Name => "cloak";

    public bool AppliesTo(string documentUri)
    {
        return true;
    }

    public IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(index);

        if (!index.Documents.TryGetValue(documentUri, out var docIndex))
            return [];

        var facts = new List<XmlFact>();
        var resolver = new EffectiveObjectResolver(index, schema, tagSource);
        var models = new HardpointBoneModelResolver(index, schema, tagSource);
        var nodesById = BuildNodeIndex(document.Html);

        foreach (var symbol in docIndex.Symbols)
        {
            if (!nodesById.TryGetValue(symbol.Id, out var node))
                continue;

            var unit = resolver.Resolve(symbol.Id, GameTypeNames.GameObjectType);
            if (!unit.Found || unit.Cyclic)
                continue;

            var cloaks = UnitCloaks.Of(unit);
            if (cloaks.Count == 0)
                continue;

            CheckParts(symbol.Id, node, cloaks, models, index, documentUri, document, facts);

            if (cloaks.Any(c => c.IsForceCloak))
                CheckAttachedModels(symbol.Id, node, unit, resolver, documentUri, document, facts);
        }

        return facts;
    }

    private static void CheckParts(
        string objectId,
        HtmlNode node,
        IReadOnlyList<UnitCloak> cloaks,
        HardpointBoneModelResolver models,
        GameIndex index,
        string documentUri,
        ParsedXmlDocument document,
        List<XmlFact> facts)
    {
        // Nothing catalogued at all: undecidable, the same guard every model-backed rule uses.
        if (index.ModelBones.IsEmpty)
            return;

        var model = models.DeclaredModels(objectId).FirstOrDefault(m => !string.IsNullOrEmpty(m));
        if (model is null)
            return;

        var key = ModelBoneKey.From(model);
        if (!index.ModelBones.TryGetValue(key, out var names))
            return;

        var all = index.ModelProxies.TryGetValue(key, out var proxies) ? names.AddRange(proxies) : names;
        var hasBaseMesh = all.Any(n => n.Contains(BaseMeshMark, StringComparison.OrdinalIgnoreCase));
        var hasStealth = all.Any(n => n.Contains(StealthMark, StringComparison.OrdinalIgnoreCase));

        foreach (var cloak in cloaks)
        {
            var (line, column, length) = CloakAnchor(node, cloak.Element, document);

            if (!hasBaseMesh)
                facts.Add(new CloakHidesNothingFact(documentUri, line, column, length,
                    objectId, model, cloak.Element, cloak.LookChangesNothing));

            if (!hasStealth)
                facts.Add(new CloakShowsNoShellFact(documentUri, line, column, length,
                    objectId, model, cloak.Element, cloak.LookChangesNothing));
        }
    }

    private static void CheckAttachedModels(
        string objectId,
        HtmlNode node,
        EffectiveObject unit,
        EffectiveObjectResolver resolver,
        string documentUri,
        ParsedXmlDocument document,
        List<XmlFact> facts)
    {
        if (unit.ValueOf(HardPointsTag) is not { } raw)
            return;

        foreach (var hardpointId in XmlUtility.SplitList(raw).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hardpoint = resolver.Resolve(hardpointId, GameTypeNames.HardPoint);
            if (!hardpoint.Found || hardpoint.Cyclic)
                continue;

            var attached = hardpoint.ValueOf(ModelToAttachTag)?.Trim();
            if (string.IsNullOrEmpty(attached))
                continue;

            var (line, column, length) = HardpointAnchor(node, hardpointId, document)
                                         ?? CloakAnchor(node, UnitCloaks.ForceCloakElement, document);

            facts.Add(new ForceCloakKeepsAttachedModelFact(documentUri, line, column, length,
                objectId, hardpointId, attached));
        }
    }

    /// <summary>
    ///     The cloak's block where this document writes it, else the object's own tag: an inherited
    ///     cloak has nothing in this file to underline but the object that inherits it.
    /// </summary>
    private static (int Line, int Column, int Length) CloakAnchor(
        HtmlNode node, string element, ParsedXmlDocument document)
    {
        var block = Child(node, AbilitiesTag)?.Descendants()
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element
                                 && n.Name.Equals(element, StringComparison.OrdinalIgnoreCase));

        return TagNameAnchor(block ?? node, document);
    }

    /// <summary>The hardpoint's entry in this document's <c>HardPoints</c> list, or null when it is inherited.</summary>
    private static (int Line, int Column, int Length)? HardpointAnchor(
        HtmlNode node, string hardpointId, ParsedXmlDocument document)
    {
        var list = Child(node, HardPointsTag);
        if (list is null)
            return null;

        var entry = XmlUtility.SplitListWithOffsets(list.InnerHtml)
            .FirstOrDefault(t => t.Token.Equals(hardpointId, StringComparison.OrdinalIgnoreCase));

        return entry.Token is null
            ? null
            : XmlUtility.GetInnerOffsetValuePosition(list, entry.Offset, entry.Token.Length, document.LineIndex);
    }

    private static (int Line, int Column, int Length) TagNameAnchor(HtmlNode element, ParsedXmlDocument document)
    {
        // Through the document's line index rather than HAP's per-node line and column, which are
        // unreliable for nested elements. One past the bracket is the tag name.
        var (line, column) = document.LineIndex.GetPosition(element.StreamPosition + 1);
        return (line, column, element.Name.Length);
    }

    private static HtmlNode? Child(HtmlNode node, string tag)
    {
        return node.ChildNodes.LastOrDefault(child =>
            child.NodeType == HtmlNodeType.Element
            && child.Name.Equals(tag, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, HtmlNode> BuildNodeIndex(HtmlDocument html)
    {
        var byId = new Dictionary<string, HtmlNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in html.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var id = XmlUtility.GetNameAttributeValue(node);
            if (!string.IsNullOrEmpty(id))
                byId.TryAdd(id, node);
        }

        return byId;
    }
}