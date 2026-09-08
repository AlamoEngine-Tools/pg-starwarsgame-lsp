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
using PG.StarWarsGame.LSP.Server.Abilities;
using PG.StarWarsGame.LSP.Server.Icons;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.ShipNames;
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
    // Resolving the workspace root and asking for its catalog is shared with the XML diagnostics
    // and the model preview - see WorkspaceIconCatalog, which is also where the bug lived that had
    // this asking DI for a concrete type nothing registers.
    private readonly IWorkspaceIconCatalog? _catalog;

    // Still needed for the ship-name catalog, which resolves its own root. The INTERFACE, not the
    // concrete service: only `IModProjectReloadService` is registered, so asking for the class
    // handed this a permanent null and every project-configured path silently fell back.
    private readonly IModProjectReloadService? _projects;
    private readonly ISchemaProvider _schema;
    private readonly IVariantTagSource _tagSource;

    private readonly IShipNameCatalogProvider? _shipNames;

    public GetEncyclopediaEntryHandler(IGameIndexService indexService, ISchemaProvider schema,
        IVariantTagSource tagSource, ILspConfigurationProvider config,
        IWorkspaceIconCatalog? catalog = null,
        IShipNameCatalogProvider? shipNames = null,
        IModProjectReloadService? projects = null)
    {
        _indexService = indexService;
        _schema = schema;
        _tagSource = tagSource;
        _config = config;
        _catalog = catalog;
        _shipNames = shipNames;
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

        // The pool an object draws its individual name from, if it is registered for one. NOT
        // picked here - see EncyclopediaShipNames for why that is the client's call.
        var shipNames = ResolveShipNames(resolver, effective.ObjectId);

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
            ResolveChrome(catalog, layout),
            shipNames);
    }

    /// <summary>
    ///     The object's ship-name pool and the name drawn from it, or <see langword="null" /> when it
    ///     is not registered for custom names.
    /// </summary>
    /// <remarks>
    ///     The wiring is read from the GameConstants SINGLETON through the same resolver the rest of
    ///     the card uses, so a mod shipping its own GameConstants.xml shadows the base game's list
    ///     without anything here knowing about layers.
    /// </remarks>
    private EncyclopediaShipNames? ResolveShipNames(EffectiveObjectResolver resolver, string objectId)
    {
        if (_shipNames is null)
            return null;

        var root = _projects?.LastWorkspaceRoots?.FirstOrDefault() ?? _config.Current.WorkspaceRoot;
        if (string.IsNullOrEmpty(root))
            return null;

        try
        {
            var constants = resolver.Resolve(EncyclopediaTags.GameConstantsId);
            if (!constants.Found)
                return null;

            var pool = _shipNames
                .Get(root, TagValue(constants, EncyclopediaTags.ShipNameTextFiles))
                .For(objectId);

            return pool is null
                ? null
                : new EncyclopediaShipNames(pool.SourcePath, pool.FileFound, pool.Names);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // The card is perfectly readable showing the class line; a broken name file must not
            // cost the caller the whole entry.
            return null;
        }
    }

    /// <summary>
    ///     The project's icon catalog, or <see langword="null" /> when icons are unavailable for any
    ///     reason. Fetched once per request and shared by the portrait, the ability slots and the
    ///     Against panels.
    /// </summary>
    private Task<IconCatalog?> GetCatalogAsync(CancellationToken ct)
    {
        return _catalog?.GetAsync(ct) ?? Task.FromResult<IconCatalog?>(null);
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
    private static EncyclopediaChrome? ResolveChrome(IconCatalog? catalog, EncyclopediaLayout layout)
    {
        if (catalog is null)
            return null;

        EncyclopediaImage? Cut(string name)
        {
            var resolved = catalog.Resolve(name);
            return resolved is null ? null : Image(resolved);
        }

        // Slots come from the XML, not from the atlas: a declared frame with no art keeps its place
        // so the switch still offers it and the indices after it do not shift.
        var frames = layout.FactionFrameTextureNames
            .Select((name, slot) => new EncyclopediaFactionFrame(
                slot, name, EncyclopediaFactionFrame.SlotNameFor(slot), Cut(name)))
            .ToArray();

        var chrome = new EncyclopediaChrome(
            // The only chrome name the shipped XML actually states, so the only one read from data.
            Cut(layout.BackdropTextureName),
            Cut("E_TOPBAR.TGA"),
            Cut("E_TOPBAR2.TGA"),
            Cut("E_LINE.TGA"),
            Cut("E_AGAINST_FRAME.TGA"),
            Cut("E_UNIT_AGAINST.TGA"),
            frames);

        // Nothing resolved at all: send null rather than a record of nulls, so the client's
        // "do I have chrome?" check stays a single test. Declared-but-artless faction slots do not
        // count as chrome - they carry no pixels for the card to draw.
        return chrome is
               {
                   Background: null, TopBar: null, TopBarNoBlip: null,
                   Line: null, AgainstFrame: null, UnitAgainst: null,
               }
               && frames.All(f => f.Image is null)
            ? null
            : chrome;
    }

    private static EncyclopediaImage? ResolveAbilityIcon(
        IconCatalog? catalog, string type, string? alternateIconName)
    {
        // The rules themselves live in AbilityIconResolver, shared with the model preview's
        // Gameplay lens: two copies of this precedence would be two places for it to drift.
        var resolved = AbilityIconResolver.Resolve(catalog, type, alternateIconName);
        return resolved.Outcome switch
        {
            AbilityIconOutcome.Resolved => Image(resolved.Icon!),

            // A declared override that does not resolve gets the missing-icon placeholder, scaled
            // into the slot - that is what the engine itself draws, so showing it is fidelity
            // rather than an error report.
            AbilityIconOutcome.DeclaredButMissing => Image(FallbackIcon.Png),

            // Ambiguous: the type-name guess missed, which is not evidence of missing art. The slot
            // asserts neither and falls back to the ability type as text.
            _ => null
        };
    }

    private static IReadOnlyList<EncyclopediaAbility> ResolveAbilities(
        EffectiveObject effective, IconCatalog? catalog)
    {
        // Through the shared reader. This walked the block with its own HAP pass until the preview
        // needed the same list, and two walkers is two places for "which child names the type" to
        // drift apart.
        var fragment = effective.Tags.FirstOrDefault(t =>
                string.Equals(t.TagName, EncyclopediaTags.UnitAbilitiesData,
                    StringComparison.OrdinalIgnoreCase))
            ?.Fragment;

        return
        [
            .. UnitAbilityReader.Read(fragment).Select(ability =>
            {
                var alternateIconName = ability.Tag("Alternate_Icon_Name");

                return new EncyclopediaAbility(
                    ability.Type,
                    ability.Tag("GUI_Activated_Ability_Name"),
                    alternateIconName,
                    ResolveAbilityIcon(catalog, ability.Type, alternateIconName));
            })
        ];
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

