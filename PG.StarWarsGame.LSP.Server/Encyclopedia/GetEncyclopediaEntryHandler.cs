// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Server.Encyclopedia;

/// <summary>
///     Resolves the text the game would show in a GameObject's encyclopedia popup.
/// </summary>
/// <remarks>
///     The popup body is authored text, not computed: <c>Encyclopedia_Text</c> holds a list of
///     localisation keys and the engine draws one line per key. Nothing here parses that text -
///     the "=====TRAITS=====" bars a modder types are lines like any other, and inferring headings
///     from separator glyphs would guess differently for every mod.
///     <para>
///         Resolution runs against the effective object so that variants inherit their encyclopedia
///         tags; reading the raw node would preview every <c>Variant_Of_Existing_Type</c> as blank.
///     </para>
///     <para>
///         Gated on <c>features.tools.encyclopedia</c>: when off, every request answers with the
///         not-found shape.
///     </para>
/// </remarks>
public sealed class GetEncyclopediaEntryHandler
    : IJsonRpcRequestHandler<GetEncyclopediaEntryParams, GetEncyclopediaEntryResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly IGameIndexService _indexService;
    private readonly ISchemaProvider _schema;
    private readonly IVariantTagSource _tagSource;

    public GetEncyclopediaEntryHandler(IGameIndexService indexService, ISchemaProvider schema,
        IVariantTagSource tagSource, ILspConfigurationProvider config)
    {
        _indexService = indexService;
        _schema = schema;
        _tagSource = tagSource;
        _config = config;
    }

    public Task<GetEncyclopediaEntryResult> Handle(GetEncyclopediaEntryParams request,
        CancellationToken cancellationToken)
    {
        if (!_config.Current.Features.Tools.Encyclopedia)
            return Task.FromResult(GetEncyclopediaEntryResult.NotFound(
                request.ObjectId, EncyclopediaLayoutResolver.Defaults));

        var index = _indexService.Current;
        var resolver = new EffectiveObjectResolver(index, _schema, _tagSource);
        var layout = new EncyclopediaLayoutResolver(resolver).Resolve();

        var effective = resolver.Resolve(request.ObjectId);
        if (!effective.Found)
            return Task.FromResult(GetEncyclopediaEntryResult.NotFound(request.ObjectId, layout));

        var loca = index.Localisation;

        // MP replaces the body wholesale, but only when it actually carries keys - an empty MP tag
        // is not an instruction to show an empty popup.
        var multiplayerBody = TagValue(effective, EncyclopediaTags.MultiplayerBody);
        var useMultiplayerBody = request.Multiplayer && !string.IsNullOrWhiteSpace(multiplayerBody);
        var body = XmlUtility
            .SplitList(useMultiplayerBody ? multiplayerBody! : TagValue(effective, EncyclopediaTags.Body) ?? string.Empty)
            .Select(key => new EncyclopediaLine(key, loca.GetValue(key)))
            .ToList();

        return Task.FromResult(new GetEncyclopediaEntryResult(
            true,
            effective.ObjectId,
            effective.TypeName,
            Translate(loca, TagValue(effective, EncyclopediaTags.TextId)),
            Translate(loca, TagValue(effective, EncyclopediaTags.UnitClass)),
            body,
            useMultiplayerBody,
            ParseCount(TagValue(effective, EncyclopediaTags.PopulationValue)),
            ResolveAbilities(effective),
            ResolveReferences(resolver, loca, TagValue(effective, EncyclopediaTags.GoodAgainst)),
            ResolveReferences(resolver, loca, TagValue(effective, EncyclopediaTags.VulnerableTo)),
            layout));
    }

    private static string? TagValue(EffectiveObject effective, string tagName)
    {
        return effective.Tags
            .FirstOrDefault(t => string.Equals(t.TagName, tagName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    /// <summary>
    ///     Reads a whole-number tag, or null when absent or not a number. Null rather than a
    ///     defaulted 0: the preview draws a blip only when there is a real value, and inventing one
    ///     would put a wrong number on the card.
    /// </summary>
    private static int? ParseCount(string? rawValue)
    {
        return int.TryParse(rawValue?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    ///     The unit's active abilities, in document order.
    /// </summary>
    /// <remarks>
    ///     Every <c>Unit_Ability</c> is returned, and deliberately so on both counts.
    ///     <para>
    ///         Not filtered to <c>GUI_Activated_Ability_Name</c>: the whole
    ///         <c>Unit_Abilities_Data</c> list is what the unit shows as icons, and an entry lacking
    ///         a command-bar binding is still one of them.
    ///     </para>
    ///     <para>
    ///         Not truncated to the two the popup has room for either. Which ones get drawn is a
    ///         display rule, and modders park abilities past the second slot on purpose - to keep an
    ///         auto-activated one off the UI, or to drive tactical GUI grouping, which keys off
    ///         ability type. Cutting the list here would erase intent the client may want to report.
    ///     </para>
    ///     <para>
    ///         Read from the tag's verbatim fragment rather than its value, because
    ///         <c>Unit_Abilities_Data</c> is a sub-object list - the children are the data and the
    ///         value itself is only whitespace. Parsed with HAP, which lower-cases element names.
    ///     </para>
    /// </remarks>
    private static IReadOnlyList<EncyclopediaAbility> ResolveAbilities(EffectiveObject effective)
    {
        var fragment = effective.Tags.FirstOrDefault(t =>
                string.Equals(t.TagName, EncyclopediaTags.UnitAbilitiesData,
                    StringComparison.OrdinalIgnoreCase))
            ?.Fragment;
        if (string.IsNullOrWhiteSpace(fragment))
            return [];

        var doc = XmlUtility.CreateHtmlDocument(fragment);
        if (!XmlUtility.TryGetRootNode(doc, out var root) || root is null)
            return [];

        var abilities = new List<EncyclopediaAbility>();
        foreach (var node in root.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element
                                 && string.Equals(n.Name, "unit_ability",
                                     StringComparison.OrdinalIgnoreCase)))
        {
            // Type is what a slot draws while icons are out of reach, so an entry without one has
            // nothing to show - and letting it take a slot would displace the ability after it.
            var type = ChildText(node, "type");
            if (string.IsNullOrWhiteSpace(type))
                continue;

            abilities.Add(new EncyclopediaAbility(
                type,
                ChildText(node, "gui_activated_ability_name"),
                ChildText(node, "alternate_icon_name")));
        }

        return abilities;
    }

    private static string? ChildText(HtmlNode parent, string lowercaseName)
    {
        var child = parent.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element
            && string.Equals(n.Name, lowercaseName, StringComparison.OrdinalIgnoreCase));
        var text = child?.InnerText.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? Translate(ILocalisationIndex loca, string? key)
    {
        return string.IsNullOrWhiteSpace(key) ? null : loca.GetValue(key.Trim());
    }

    /// <summary>
    ///     Turns a list of object ids into display names via each target's own <c>Text_ID</c>. The
    ///     targets resolve through the variant chain too, so a referenced variant that inherits its
    ///     name still shows one.
    /// </summary>
    private static IReadOnlyList<EncyclopediaReference> ResolveReferences(
        EffectiveObjectResolver resolver, ILocalisationIndex loca, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return [];

        return XmlUtility.SplitList(rawValue)
            .Select(id =>
            {
                var target = resolver.Resolve(id);
                return new EncyclopediaReference(id,
                    target.Found ? Translate(loca, TagValue(target, EncyclopediaTags.TextId)) : null);
            })
            .ToList();
    }
}
