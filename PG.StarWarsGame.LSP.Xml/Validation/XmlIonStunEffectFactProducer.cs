// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     A type without the ION_STUN_EFFECT behaviour against the ion stun effects its model draws.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the game: effects are switched in groups by the start of their names, and the
///         ION_STUN_EFFECT behaviour is the only thing that writes the <c>pi</c> group - hidden when
///         the object is created, shown for a stun, hidden at its end. A type without the behaviour
///         draws those effects as its model file has them, so one the file marks visible is drawn
///         from the start and never hidden.
///     </para>
///     <para>
///         On the type's XML object, never on the model: a model shared by a type with the
///         behaviour and one without is only in question for the second. The models are found the way
///         every model-backed rule finds them (<see cref="HardpointBoneModelResolver.DeclaredModels" />)
///         and their effects come from the catalogue's parts. Silent for a model whose parts are
///         unknown - a baseline built before the catalogue carried them, or a file that could not
///         be read.
///     </para>
/// </remarks>
public sealed class XmlIonStunEffectFactProducer(ISchemaProvider schema, IVariantTagSource tagSource)
    : IXmlFactProducer
{
    private const string IonStunBehavior = "ION_STUN_EFFECT";

    // The group's name prefix. The game compares the start of an effect's name over the prefix's
    // length, case ignored.
    private const string IonStunGroup = "pi";

    public string Name => "ion-stun-effect";

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

        // Nothing catalogued at all: undecidable, the same guard every model-backed rule uses.
        if (index.ModelBones.IsEmpty || index.ModelParts.IsEmpty)
            return [];

        var facts = new List<XmlFact>();
        var resolver = new EffectiveObjectResolver(index, schema, tagSource);
        var models = new HardpointBoneModelResolver(index, schema, tagSource);
        var nodesById = BuildNodeIndex(document.Html);

        foreach (var symbol in docIndex.Symbols)
        {
            if (!nodesById.TryGetValue(symbol.Id, out var node))
                continue;

            var type = resolver.Resolve(symbol.Id, GameTypeNames.GameObjectType);
            if (!type.Found || type.Cyclic || ObjectBehaviors.Has(type, IonStunBehavior))
                continue;

            foreach (var model in models.DeclaredModels(symbol.Id))
            {
                if (string.IsNullOrEmpty(model))
                    continue;

                var effects = EffectsShown(index, ModelBoneKey.From(model));
                if (effects.Count == 0)
                    continue;

                var (line, column, length) = Anchor(node, document);
                facts.Add(new IonEffectsNeverHiddenFact(documentUri, line, column, length,
                    symbol.Id, model, effects));
            }
        }

        return facts;
    }

    /// <summary>
    ///     The model's effects of the ion stun group that the file marks visible. Each name once, in
    ///     file order. Empty when the model's parts are unknown.
    /// </summary>
    private static List<string> EffectsShown(GameIndex index, string key)
    {
        if (!index.ModelParts.TryGetValue(key, out var parts))
            return [];

        return
        [
            .. parts
                .Where(p => p.Proxy && p.FileVisible
                                    && p.Name.StartsWith(IonStunGroup, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    ///     The behaviour list this document writes for the object - the first of the three, in the
    ///     order they are written - else the object's own tag: an inherited list has nothing in this
    ///     file to underline but the object that inherits it.
    /// </summary>
    private static (int Line, int Column, int Length) Anchor(HtmlNode node, ParsedXmlDocument document)
    {
        var list = node.ChildNodes.FirstOrDefault(child =>
            child.NodeType == HtmlNodeType.Element
            && ObjectBehaviors.BehaviorTags.Contains(child.Name, StringComparer.OrdinalIgnoreCase));

        // Through the document's line index rather than HAP's per-node line and column, which are
        // unreliable for nested elements. One past the bracket is the tag name.
        var element = list ?? node;
        var (line, column) = document.LineIndex.GetPosition(element.StreamPosition + 1);
        return (line, column, element.Name.Length);
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