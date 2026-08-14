// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Icons;
using PG.StarWarsGame.LSP.Server.Project;
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
    private readonly IIconCatalogProvider? _icons;
    private readonly IGameIndexService _indexService;
    private readonly ModProjectReloadService? _projects;
    private readonly ISchemaProvider _schema;
    private readonly IVariantTagSource _tagSource;

    public GetEncyclopediaEntryHandler(IGameIndexService indexService, ISchemaProvider schema,
        IVariantTagSource tagSource, ILspConfigurationProvider config,
        IIconCatalogProvider? icons = null, ModProjectReloadService? projects = null)
    {
        _indexService = indexService;
        _schema = schema;
        _tagSource = tagSource;
        _config = config;
        _icons = icons;
        _projects = projects;
    }

    public async Task<GetEncyclopediaEntryResult> Handle(GetEncyclopediaEntryParams request,
        CancellationToken cancellationToken)
    {
        if (!_config.Current.Features.Tools.Encyclopedia)
            return GetEncyclopediaEntryResult.NotFound(
                request.ObjectId, EncyclopediaLayoutResolver.Defaults);

        var index = _indexService.Current;
        var resolver = new EffectiveObjectResolver(index, _schema, _tagSource);
        var layout = new EncyclopediaLayoutResolver(resolver).Resolve();

        var effective = resolver.Resolve(request.ObjectId);
        if (!effective.Found)
            return GetEncyclopediaEntryResult.NotFound(request.ObjectId, layout);

        var catalog = await GetCatalogAsync(cancellationToken);
        var loca = index.Localisation;

        // MP replaces the body wholesale, but only when it actually carries keys - an empty MP tag
        // is not an instruction to show an empty popup.
        var multiplayerBody = TagValue(effective, EncyclopediaTags.MultiplayerBody);
        var useMultiplayerBody = request.Multiplayer && !string.IsNullOrWhiteSpace(multiplayerBody);
        var body = XmlUtility
            .SplitList(useMultiplayerBody ? multiplayerBody! : TagValue(effective, EncyclopediaTags.Body) ?? string.Empty)
            .Select(key => new EncyclopediaLine(key, loca.GetValue(key)))
            .ToList();

        return new GetEncyclopediaEntryResult(
            true,
            effective.ObjectId,
            effective.TypeName,
            Translate(loca, TagValue(effective, EncyclopediaTags.TextId)),
            Translate(loca, TagValue(effective, EncyclopediaTags.UnitClass)),
            body,
            useMultiplayerBody,
            ParseCount(TagValue(effective, EncyclopediaTags.PopulationValue)),
            ResolveAbilities(effective, catalog),
            ResolveReferences(resolver, loca, catalog, TagValue(effective, EncyclopediaTags.GoodAgainst)),
            ResolveReferences(resolver, loca, catalog, TagValue(effective, EncyclopediaTags.VulnerableTo)),
            layout,
            ResolveIcon(catalog, effective),
            ResolveChrome(catalog));
    }

    /// <summary>
    ///     The project's icon catalog, or <see langword="null" /> when icons are unavailable for any
    ///     reason. Fetched once per request and shared by the portrait, the ability slots and the
    ///     Against panels.
    /// </summary>
    private async Task<IconCatalog?> GetCatalogAsync(CancellationToken ct)
    {
        if (_icons is null)
            return null;

        var root = _projects?.LastWorkspaceRoots?.FirstOrDefault() ?? _config.Current.WorkspaceRoot;
        if (string.IsNullOrEmpty(root))
            return null;

        try
        {
            return await _icons.GetAsync(root, _projects?.LastWorkspaceConfig?.Icons, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static EncyclopediaIcon? ResolveIcon(IconCatalog? catalog, EffectiveObject effective)
    {
        var iconName = TagValue(effective, EncyclopediaTags.IconName)?.Trim();
        if (catalog is null || string.IsNullOrEmpty(iconName))
            return null;

        try
        {
            var resolved = catalog.Resolve(iconName);

            // The object names an icon, so the card should show its portrait slot either way. When
            // nothing supplied one - no baseline, a mod .mtd that omits it, art that was never
            // drawn - the placeholder keeps the layout honest and says so, rather than silently
            // collapsing to a card that looks like the object never had an icon at all.
            if (resolved is null)
            {
                var placeholder = Image(FallbackIcon.Png);
                return new EncyclopediaIcon(
                    iconName, placeholder.DataUri, FallbackSource, false,
                    placeholder.Width, placeholder.Height);
            }

            return new EncyclopediaIcon(
                iconName,
                DataUri(resolved.Png),
                resolved.Source.ToString(),
                resolved.IsMegaTextureStale,
                resolved.Width,
                resolved.Height);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Reported as the icon's source when the built-in placeholder stood in. Not a member of
    ///     <see cref="IconSource" />, which describes where real pixels came from - this says the
    ///     opposite, that none were found.
    /// </summary>
    private const string FallbackSource = "Fallback";

    private static string DataUri(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);

    /// <summary>Packages a resolved icon with the natural size the card lays out against.</summary>
    private static EncyclopediaImage Image(IconResolution resolved) =>
        new(DataUri(resolved.Png), resolved.Width, resolved.Height);

    /// <summary>The same for bytes with no resolution behind them - the built-in placeholder.</summary>
    private static EncyclopediaImage Image(byte[] png)
    {
        PngHeader.TryRead(png, out var width, out var height);
        return new EncyclopediaImage(DataUri(png), width, height);
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
    /// <summary>
    ///     The icon the engine draws for an ability of <paramref name="type" />, if we can find one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A HEURISTIC, and knowingly so. Ability icons are named <c>I_SA_&lt;something&gt;</c> in
    ///         the mega texture, but nothing in the game's data connects an ability to its icon -
    ///         <c>I_SA_*</c> occurs in exactly one file across an install, the .mtd itself, so the
    ///         engine hardcodes the mapping and there is nothing to read. Guessing
    ///         <c>I_SA_&lt;TYPE&gt;</c> matches 43 of the ability types FoC ships.
    ///     </para>
    ///     <para>
    ///         Safe despite being a guess, because the catalog validates it: a name only resolves if
    ///         that exact entry exists, so a wrong guess yields NO icon rather than the WRONG one.
    ///         The misses are real naming variants - <c>BARRAGE</c> has no <c>I_SA_BARRAGE</c> though
    ///         the atlas carries <c>I_SA_BARRAGE_AREA</c>, and the <c>I_SA_BL_*</c> family takes a
    ///         prefix no type name has. Those slots keep showing the type as text, exactly as before.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     The card's own chrome from the atlas. Null throughout when icons are unavailable, and
    ///     null per field when that entry is absent - the client keeps its CSS rendition either way,
    ///     so this can only improve the card, never break it.
    /// </summary>
    private static EncyclopediaChrome? ResolveChrome(IconCatalog? catalog)
    {
        if (catalog is null)
            return null;

        EncyclopediaImage? Cut(string name)
        {
            var resolved = catalog.Resolve(name);
            return resolved is null ? null : Image(resolved);
        }

        var chrome = new EncyclopediaChrome(
            Cut("E_BACKGROUND.TGA"),
            Cut("E_TOPBAR.TGA"),
            Cut("E_TOPBAR2.TGA"),
            Cut("E_LINE.TGA"),
            Cut("E_AGAINST_FRAME.TGA"),
            Cut("E_UNIT_AGAINST.TGA"));

        // Nothing resolved at all: send null rather than a record of six nulls, so the client's
        // "do I have chrome?" check stays a single test.
        return chrome is
        {
            Background: null, TopBar: null, TopBarNoBlip: null,
            Line: null, AgainstFrame: null, UnitAgainst: null,
        }
            ? null
            : chrome;
    }

    private static EncyclopediaImage? ResolveAbilityIcon(
        IconCatalog? catalog, string type, string? alternateIconName)
    {
        if (catalog is null)
            return null;

        // An Alternate_* tag REPLACES the default it shadows - the same holds for the ability's name
        // and description - so when one names an icon, that icon IS the ability's icon and the
        // type-name guess never applies. It is per-instance, not per-type: two units can give the
        // same ability type different icons this way.
        if (!string.IsNullOrWhiteSpace(alternateIconName))
        {
            var overridden = catalog.Resolve(alternateIconName.Trim());

            // A declared override that does not resolve gets the missing-icon placeholder, scaled
            // into the slot - that is what the engine itself draws, so showing it is fidelity
            // rather than an error report. Substituting the default would be strictly worse: it
            // would hide a broken reference behind a plausible icon the game would never show.
            return overridden is null ? Image(FallbackIcon.Png) : Image(overridden);
        }

        // A confirmed exception wins over the type-name convention. These were checked against the
        // running game, not inferred - several could not be guessed at all (INVULNERABILITY draws
        // EVASIVE_MANEUVERS) and one leans on a misspelling Petroglyph shipped.
        var known = AbilityIconNames.For(type);
        if (known is not null)
        {
            var mapped = catalog.Resolve(known);
            if (mapped is not null)
                return Image(mapped);
        }

        // A miss HERE is AMBIGUOUS, which is why it draws neither the icon nor the placeholder.
        // The engine resolves its hardcoded name out of the same mega texture, so a missing entry
        // does make it draw MISSING - but I_SA_<TYPE> is only a guess at that name, and a miss can
        // equally mean the real name is something else entirely (BARRAGE's icon is I_SA_BARRAGE_AREA)
        // while the game shows perfectly good art. We cannot tell those apart without knowing the
        // hardcoded name, so the slot asserts neither and falls back to the ability type as text.
        var resolved = catalog.Resolve("I_SA_" + type.Trim().ToUpperInvariant());
        return resolved is null ? null : Image(resolved);
    }

    private static IReadOnlyList<EncyclopediaAbility> ResolveAbilities(
        EffectiveObject effective, IconCatalog? catalog)
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

            var alternateIconName = ChildText(node, "alternate_icon_name");
            abilities.Add(new EncyclopediaAbility(
                type,
                ChildText(node, "gui_activated_ability_name"),
                alternateIconName,
                ResolveAbilityIcon(catalog, type, alternateIconName)));
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
        EffectiveObjectResolver resolver, ILocalisationIndex loca, IconCatalog? catalog, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return [];

        return XmlUtility.SplitList(rawValue)
            .Select(id =>
            {
                // A second hop through the TARGET object: the panel shows what that unit looks like
                // and is called, so both its name and its portrait come from its own tags rather
                // than anything on the object being previewed.
                var target = resolver.Resolve(id);
                if (!target.Found)
                    return new EncyclopediaReference(id, null, null);

                // Each entry is a unit reference, so its slot art is just that unit's own icon -
                // the same resolution the card's portrait goes through, placeholder included.
                return new EncyclopediaReference(
                    id,
                    Translate(loca, TagValue(target, EncyclopediaTags.TextId)),
                    ResolveIcon(catalog, target)?.DataUri);
            })
            .ToList();
    }
}

