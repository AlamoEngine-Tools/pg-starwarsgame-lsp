// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Reports an object that omits a tag its own <c>Validate_Data</c> refuses to run without.
/// </summary>
/// <remarks>
///     <para>
///         Scoped to the ELEMENT rather than to the tag, and that is the whole design. The engine
///         states each of these requirements inside one ability class, and the same tag names recur
///         on other abilities that carry no such rule - <c>Beam_Texture_Name</c> on
///         <c>Super_Laser_Ability</c>, <c>Bomb_Type</c> on <c>Cluster_Bomb_Ability</c>. A
///         tag-scoped rule would warn on objects that are configured correctly.
///     </para>
///     <para>
///         Which element owns a requirement is taken from the xref to the engine's own message
///         string, never from where our schema happens to declare the tag. The two answers differ:
///         our schema carries <c>Bomb_Type</c> on three types and only
///         <c>DemolitionAbilityClass::Validate_Data</c> demands it.
///     </para>
/// </remarks>
public abstract class RequiredTagsRuleBase : IXmlCrossTagRule
{
    /// <summary>The element as HAP reports it - lower-cased and underscored.</summary>
    protected abstract string ElementName { get; }

    /// <summary>The schema type name, for the message.</summary>
    protected abstract string OwningType { get; }

    /// <summary>The tags this element cannot run without, each with its own engine message.</summary>
    protected abstract IReadOnlyList<string> RequiredTags { get; }

    /// <summary>
    ///     What the engine substitutes when the tag is absent, as a sentence fragment. Empty when
    ///     it only complains.
    /// </summary>
    protected virtual string Repair => string.Empty;

    /// <summary>
    ///     The value the engine writes in when the tag is absent, where it writes one. Null for the
    ///     tags it only complains about - most of them - and the reason the quick fix is offered for
    ///     one rule in this family and not the rest.
    /// </summary>
    protected virtual string? DefaultValue => null;

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!string.Equals(objectNode.Name, ElementName, StringComparison.OrdinalIgnoreCase))
            return [];

        var facts = new List<XmlFact>();
        foreach (var tag in RequiredTags)
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
                tag,
                Repair,
                BuildInsertion(objectNode, lineIndex, tag)));
        }

        return facts;
    }

    /// <summary>
    ///     The engine's default written into the document, where there is one to write.
    /// </summary>
    private XmlEngineRepair? BuildInsertion(HtmlNode objectNode, LineOffsetIndex lineIndex, string tag)
    {
        if (DefaultValue is not { } value) return null;

        var edit = TagInsertionFactory.InsertLastChild(objectNode, lineIndex, tag, value);

        return edit is null
            ? null
            : new XmlEngineRepair($"Apply the engine's own default: <{tag}>{value}</{tag}>", [edit]);
    }
}
