// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <inheritdoc />
public sealed class XmlDamageStageFactProducer(ISchemaProvider schema, IVariantTagSource tagSource)
    : IXmlDamageStageFactProducer
{
    private const string AlternatesTag = "Land_Damage_Alternates";

    public IReadOnlyList<XmlFact> Produce(
        string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(index);

        if (!index.Documents.TryGetValue(documentUri, out var docIndex))
            return [];

        // Nothing catalogued at all - the game's models were never indexed, or this workspace has
        // none. Undecidable rather than wrong, and the same guard the hardpoint producer uses.
        if (index.ModelBones.IsEmpty)
            return [];

        var facts = new List<XmlFact>();
        var models = new HardpointBoneModelResolver(index, schema, tagSource);
        var nodesById = BuildNodeIndex(document.Html);

        foreach (var symbol in docIndex.Symbols)
        {
            if (!nodesById.TryGetValue(symbol.Id, out var node))
                continue;

            // The tag as THIS document writes it. Read off the node rather than off the effective
            // object because the diagnostic needs a range in this file - a variant that inherits its
            // alternates has nothing here to underline, and the object that declares them does.
            var alternates = node.ChildNodes.LastOrDefault(child =>
                child.NodeType == HtmlNodeType.Element
                && child.Name.Equals(AlternatesTag, StringComparison.OrdinalIgnoreCase));

            if (alternates is null)
                continue;

            Check(symbol.Id, alternates, models, index, documentUri, document, facts);
        }

        return facts;
    }

    private static void Check(
        string objectId,
        HtmlNode alternates,
        HardpointBoneModelResolver models,
        GameIndex index,
        string documentUri,
        ParsedXmlDocument document,
        List<XmlFact> facts)
    {
        // The MODEL is variant-resolved, unlike the tag: a variant may inherit the model it stages
        // against, and checking a declared list against nothing would be a false warning.
        var model = models.DeclaredModels(objectId).FirstOrDefault(m => !string.IsNullOrEmpty(m));
        if (model is null)
            return;

        if (!index.ModelBones.TryGetValue(ModelBoneKey.From(model), out var names))
            return;

        var tagged = ModelLevelTag.AltLevelsIn(names);
        var raw = alternates.InnerHtml;
        var declared = new List<int>();

        foreach (var (token, _) in XmlUtility.SplitListWithOffsets(raw))
            if (int.TryParse(token, out var stage))
                declared.Add(stage);

        var missing = ModelLevelTag.StagesNotTagged(declared, tagged);
        if (missing.Count == 0)
            return;

        // Anchored on the first missing STAGE inside the list rather than on the whole tag: the list
        // is positional and sits beside two others, so underlining all of it says the table is wrong
        // when one entry is.
        var anchor = XmlUtility.SplitListWithOffsets(raw)
            .FirstOrDefault(t => int.TryParse(t.Token, out var stage) && stage == missing[0]);

        var (line, column, length) = anchor.Token is null
            ? XmlUtility.GetValuePosition(alternates, document.LineIndex)
            : XmlUtility.GetInnerOffsetValuePosition(
                alternates, anchor.Offset, anchor.Token.Length, document.LineIndex);

        facts.Add(new DamageStageNotOnModelFact(documentUri, line, column, length,
            objectId, model, missing, [.. tagged]));
    }

    /// <summary>
    ///     Every named object in the document, keyed by id, in one walk.
    /// </summary>
    /// <remarks>
    ///     The same shape as the hardpoint producer's index and for the same reason: a fresh
    ///     <c>Descendants()</c> scan per symbol re-walks the whole tree once per object.
    /// </remarks>
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
