// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>
///     What one entry relates to (#270), computed for that entry alone when it is opened: the
///     entries its tags name, by what each is, what names it, and every layer that defines it.
/// </summary>
/// <remarks>
///     An object's tags come through the effective-object resolver, so a variant names what its base
///     names; one object is one cached document parse. A tag is put in a relation by what its schema
///     definition says it references, slot by slot for a tuple.
/// </remarks>
/// <param name="proxiesOf">
///     The proxy names a model file carries, by its game-relative path; a model's particle systems
///     are read from it, one file, when the model is opened.
/// </param>
/// <param name="tools">The tool features on; an action whose feature is off is offered disabled.</param>
/// <param name="fileOf">
///     The file on disk an asset or a script is, by its entry id - a game-relative path or a document
///     URI; null where none is, as for one that only ships inside the game's archives.
/// </param>
public sealed class SymbolRelations(
    GameIndex index,
    ISchemaProvider schema,
    IVariantTagSource tags,
    SymbolCatalog catalog,
    Func<string, IReadOnlyList<string>>? proxiesOf = null,
    ToolsFeatureFlags? tools = null,
    Func<string, string?>? fileOf = null)
{
    private const string GameObjects = "xml:GameObjectType";
    private const string ReferencedBy = "referencedBy";
    private const string Definitions = "definitions";
    private const string UsedBy = "usedBy";
    private const string Off = "off";
    private static readonly char[] s_separators = [',', ' ', '\t', '\n', '\r'];
    private static readonly string[] s_textureInterchange = [".tga", ".dds"];

    private readonly ToolsFeatureFlags _tools = tools ?? new ToolsFeatureFlags();

    // Which objects name each asset: every object's tags, read once per index on the first request
    // that needs it, never on open (about 830 ms on the EaW tree - measured in code comments).
    private static readonly ConditionalWeakTable<GameIndex, Dictionary<string, SortedSet<string>>> s_users = new();

    /// <summary>The relations an asset, a script or an enum opens into, by page.</summary>
    private static readonly Dictionary<string, string[]> s_assetRelations = new(StringComparer.Ordinal)
    {
        ["models"] = ["animations", "particleSystems", "textures", UsedBy],
        ["particleSystems"] = ["textures", UsedBy],
        ["animations"] = ["model"],
        ["textures"] = [UsedBy],
        ["audio"] = [UsedBy],
        ["luaScripts"] = ["loadedBy", "requires"],
        ["enums"] = ["values", UsedBy],
        ["enumValues"] = [UsedBy],
        ["schemaTypes"] = ["tags"],
        ["schemaTags"] = [],
        ["behaviours"] = ["objects"]
    };

    /// <summary>Whether a page lists the schema's own elements: no file of their own, nothing to reveal.</summary>
    private static bool IsSchema(string page)
    {
        return page is "enums" or "enumValues" or "schemaTypes" or "schemaTags" or "behaviours";
    }

    /// <summary>The relations a game object always shows, empty ones disabled with their reason.</summary>
    private static readonly string[] s_objectRelations =
    [
        "models", "particleSystems", "particles", "sfxEvents", "speechEvents", "hardpoints", "otherReferences",
        "textures", "luaScript"
    ];

    public GetSymbolNodeResult Node(string page, string id)
    {
        if (catalog.Find(page, id) is not { } entry) return new GetSymbolNodeResult(null, []);
        if (!page.StartsWith("xml:", StringComparison.Ordinal))
        {
            // An enum, a type, a tag and a behaviour are the schema's, no file of their own.
            var file = IsSchema(page) ? null : fileOf?.Invoke(entry.Id);
            return new GetSymbolNodeResult(entry, (s_assetRelations.GetValueOrDefault(page) ?? [])
                .Select(r => NeedsEveryObject(page, r) && !s_users.TryGetValue(index, out _)
                    ? new SymbolRelation(r, null, null)
                    : Counted(r, AssetRows(page, entry, r).Count, "none"))
                .ToList())
            {
                Location = page == "luaScripts" ? new SymbolLocation(entry.Id, 0) : null,
                Actions = AssetActions(page, entry, file),
                Open = new SymbolAction("open", SymbolPreviews.HasPreview(page) ? null : "noPreview"),
                File = file
            };
        }

        var (named, known) = TagRelations(entry);
        var keys = page == GameObjects
            ? s_objectRelations
            : named.Where(r => r.Value.Count > 0).Select(r => r.Key).ToArray();
        var relations = keys
            .Select(k => Counted(k, named.GetValueOrDefault(k)?.Count ?? 0, known ? "none" : "notInModFiles"))
            .Append(Counted(ReferencedBy, ReferencingObjects(entry.Id).Count(), "none"))
            .Append(Counted(Definitions, DefinitionRows(entry.Id).Count, "none"))
            .ToList();
        var defined = WorkspaceDefinition(entry.Id);
        var winner = defined ?? index.Baseline.Symbols.GetValueOrDefault(entry.Id);
        // Only a workspace definition is a document to go to: the baseline's origin is the game's own
        // path of the file, inside its packed data.
        var location = defined?.Origin is FileOrigin origin ? new SymbolLocation(origin.Uri, origin.Line) : null;
        return new GetSymbolNodeResult(entry, relations)
        {
            Location = location,
            VariantOf = winner?.VariantBaseId,
            Actions = ObjectActions(winner, location, page == GameObjects)
        };
    }

    // A script is a text file of the project; an asset is a file the game reads, an enum the schema's.
    // Revealing a file in the file manager is every file's last action: one that ships only inside
    // the game's archives has no file to show, and one listed but not found says so.
    private List<SymbolAction> AssetActions(string page, SymbolRow entry, string? file)
    {
        var definition = new SymbolAction("definition", page switch
        {
            "luaScripts" => null,
            _ when IsSchema(page) => "schema",
            _ => "binary"
        });
        if (IsSchema(page)) return [definition];
        List<SymbolAction> actions = page switch
        {
            "models" or "particleSystems" =>
                [definition, new SymbolAction("previewModel", _tools.ModelPreview ? null : Off)],
            // A clip is worth seeing only on its model, which the preview pairs with it by name and
            // checks against the skeleton; one no model's name prefixes has nothing to play on.
            "animations" =>
            [
                definition, new SymbolAction("previewClip", !_tools.ModelPreview
                    ? Off
                    : ModelOf(entry.Name).Any()
                        ? null
                        : "unpaired")
            ],
            _ => [definition]
        };
        actions.Add(new SymbolAction("reveal", file is not null
            ? null
            : entry.Origin == "game"
                ? "packed"
                : "notFound"));
        return actions;
    }

    // The code lens set, on the lenses' own rules. Assembling a unit and drawing its encyclopedia
    // card are a game object's; another type does not get them at all, rather than disabled.
    private List<SymbolAction> ObjectActions(GameSymbol? winner, SymbolLocation? location, bool gameObject)
    {
        List<SymbolAction> actions =
        [
            new SymbolAction("definition", location is null ? "shipped" : null),
            new SymbolAction("previewUnit", !_tools.ModelPreview
                ? Off
                : winner is not null && TacticalModels.Declared(index, tags, winner)
                    ? null
                    : "noModel"),
            new SymbolAction("encyclopedia", !_tools.Encyclopedia
                ? Off
                : winner is null || !EncyclopediaTags.DefinesBody(index, tags, winner)
                    ? "noEncyclopedia"
                    : EncyclopediaTags.HasCard(index, tags, winner, schema.GetKind(ObjectKindNames.Planet))
                        ? null
                        : "planet"),
            new SymbolAction("effectiveObject", !_tools.Variants
                ? Off
                : string.IsNullOrEmpty(winner?.VariantBaseId)
                    ? "notAVariant"
                    : null)
        ];
        return gameObject
            ? actions
            : actions.Where(a => a.Action is not ("previewUnit" or "encyclopedia")).ToList();
    }

    // The highest layer's definition - with the game's, the one the catalog lists.
    private GameSymbol? WorkspaceDefinition(string id)
    {
        return index.WorkspaceDefinitions.TryGetValue(id, out var definitions)
            ? definitions.Where(s => s is { Kind: GameSymbolKind.XmlObject, Origin: FileOrigin })
                .OrderByDescending(index.LayerRankOf)
                .FirstOrDefault()
            : null;
    }

    /// <summary>The rows of one relation of an entry; null for an entry or relation there is not.</summary>
    public IReadOnlyList<SymbolRow>? Rows(string page, string id, string relation)
    {
        if (catalog.Find(page, id) is not { } entry) return null;
        if (!page.StartsWith("xml:", StringComparison.Ordinal))
            return s_assetRelations.TryGetValue(page, out var relations) && relations.Contains(relation)
                ? AssetRows(page, entry, relation)
                : null;

        switch (relation)
        {
            case ReferencedBy:
                return ReferencingObjects(entry.Id).Select(o => catalog.FindObject(o) ?? Missing(o, null)).ToList();
            case Definitions:
                return DefinitionRows(entry.Id);
        }

        var (named, _) = TagRelations(entry);
        if (!named.TryGetValue(relation, out var targets) && !s_objectRelations.Contains(relation)) return null;
        return (targets ?? []).Select(t => RowOf(relation, t.Name, t.Type)).ToList();
    }

    private static bool NeedsEveryObject(string page, string relation)
    {
        return relation == UsedBy && page is "models" or "particleSystems" or "textures" or "audio";
    }

    private List<SymbolRow> AssetRows(string page, SymbolRow entry, string relation)
    {
        IEnumerable<SymbolRow> rows = (page, relation) switch
        {
            ("models", "animations") => ModelAnimationClips.For(index, entry.Name)
                .Select(c => catalog.Find("animations", c) ?? Missing(c, ".ala")),
            // A proxy names its particle system with the level tags the engine strips before loading.
            ("models", "particleSystems") => (proxiesOf?.Invoke(entry.Id) ?? [])
                .Select(p => ModelLevelTag.Strip(p) + ".alo")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => catalog.Find("particleSystems", p) ?? Missing(p.ToLowerInvariant(), ".alo")),
            (_, "textures") => index.ModelTextures.GetValueOrDefault(ModelBoneKey.From(entry.Name), [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => Texture(t) ?? Missing(t, null)),
            ("animations", "model") => ModelOf(entry.Name),
            ("enums", UsedBy) => TagsTypedWith(entry.Name),
            ("luaScripts", "loadedBy") => ObjectsReferencing(WorkspaceFileKey.Create(WorkspaceFileKey.LuaScriptType,
                entry.Name)),
            ("luaScripts", "requires") => (index.Documents.GetValueOrDefault(entry.Id)?.RequireArgs ?? [])
                .Select(a => catalog.Find("luaScripts", WithExtension(a, ".lua")) ?? Missing(a, ".lua")),
            ("textures", UsedBy) => ModelsNaming(entry.Name).Concat(Users(entry.Name)),
            ("schemaTypes", "tags") => catalog.Rows("schemaTags")
                .Where(r => r.Id.StartsWith(entry.Id + "/", StringComparison.Ordinal)),
            ("enums", "values") => catalog.Rows("enumValues")
                .Where(r => r.Id.StartsWith(entry.Id + "/", StringComparison.Ordinal)),
            // A value's users, as the parser records each tag holding it - rename's own references.
            ("enumValues", UsedBy) => ObjectsReferencing("enum:" + entry.Id),
            ("behaviours", "objects") => catalog.Rows(GameObjects)
                .Where(r => r.Kinds.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)),
            ("particleSystems", UsedBy) => ModelsCarrying(entry.Name).Concat(Users(entry.Name)),
            (_, UsedBy) => Users(entry.Name),
            _ => []
        };
        return rows.DistinctBy(r => r.Id).ToList();
    }

    // The tags typed with an enum, as the tag pages they are on each type that has them; a schema
    // that lists no types names them as tags alone.
    private IEnumerable<SymbolRow> TagsTypedWith(string enumName)
    {
        bool Takes(XmlTagDefinition tag)
        {
            return string.Equals(tag.Enum?.Name, enumName, StringComparison.OrdinalIgnoreCase);
        }

        var pages = schema.AllObjectTypes
            .SelectMany(t => schema.GetTagsForType(t.TypeName).Where(Takes)
                .Select(tag => catalog.Find("schemaTags", t.TypeName + "/" + tag.Tag)))
            .OfType<SymbolRow>()
            .ToList();
        return pages.Count > 0
            ? pages
            : schema.AllTags.Where(Takes).Select(t => new SymbolRow(t.Tag, t.Tag, "tag", [], "schema", null, []));
    }

    // The model an animation belongs to: the longest model name its own name starts with.
    private IEnumerable<SymbolRow> ModelOf(string animation)
    {
        var stem = Path.GetFileNameWithoutExtension(animation);
        var model = catalog.Rows("models")
            .Where(m => stem.StartsWith(Path.GetFileNameWithoutExtension(m.Name) + "_",
                StringComparison.OrdinalIgnoreCase))
            .MaxBy(m => m.Name.Length);
        return model is null ? [] : [model];
    }

    private IEnumerable<SymbolRow> ModelsNaming(string texture)
    {
        var key = TextureKey(texture);
        return index.ModelTextures
            .Where(m => m.Value.Any(named => TextureKey(named) == key))
            .Select(m => catalog.Find("models", m.Key) ?? catalog.Find("particleSystems", m.Key))
            .OfType<SymbolRow>()
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
    }

    // The models whose proxies name a particle system, matched as the engine loads it: level tags stripped.
    private IEnumerable<SymbolRow> ModelsCarrying(string particleSystem)
    {
        var stem = Path.GetFileNameWithoutExtension(particleSystem);
        return index.ModelProxies
            .Where(m => m.Value.Any(p => string.Equals(ModelLevelTag.Strip(p), stem,
                StringComparison.OrdinalIgnoreCase)))
            .Select(m => catalog.Find("models", m.Key))
            .OfType<SymbolRow>()
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<SymbolRow> Users(string asset)
    {
        var users = s_users.GetValue(index, _ => AllUsers());
        return users.TryGetValue(asset, out var objects)
            ? objects.Select(o => catalog.FindObject(o) ?? Missing(o, null))
            : [];
    }

    private Dictionary<string, SortedSet<string>> AllUsers()
    {
        var users = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in catalog.Objects)
        {
            var (named, _) = TagRelations(row);
            foreach (var (relation, targets) in named)
            {
                if (relation is not ("models" or "particleSystems" or "textures" or "samples")) continue;
                foreach (var (name, _) in targets)
                {
                    var asset = relation is "models" or "particleSystems" ? WithExtension(name, ".alo") : name;
                    if (!users.TryGetValue(asset, out var objects))
                        users[asset] = objects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    objects.Add(row.Id);
                }
            }
        }

        return users;
    }

    private IEnumerable<SymbolRow> ObjectsReferencing(string key)
    {
        return index.WorkspaceReferences.TryGetValue(key, out var references)
            ? references.Select(ContainingObject).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(o => catalog.FindObject(o) ?? Missing(o, null))
            : [];
    }

    private static SymbolRelation Counted(string relation, int count, string emptyReason)
    {
        return new SymbolRelation(relation, count, count == 0 ? emptyReason : null);
    }

    // The entries an object's tags name, by relation, each once; and whether its tags are known.
    private (Dictionary<string, List<(string Name, string? Type)>> Named, bool Known) TagRelations(SymbolRow entry)
    {
        var named = new Dictionary<string, List<(string Name, string? Type)>>(StringComparer.Ordinal);
        var effective = new EffectiveObjectResolver(index, schema, tags).Resolve(entry.Id);
        foreach (var tag in effective.Tags)
        {
            if (schema.GetTag(tag.TagName) is not { } definition) continue;
            var tokens = tag.Value.Split(s_separators, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                var (kind, type) = definition.Slots.Count > 0
                    ? (definition.Slots[i % definition.Slots.Count].ReferenceKind,
                        definition.Slots[i % definition.Slots.Count].ReferenceTypeName)
                    : (definition.ReferenceKind, definition.ReferenceTypeName);
                if (RelationOf(kind, type, tokens[i]) is not { } relation) continue;
                if (!named.TryGetValue(relation, out var list)) named[relation] = list = [];
                if (!list.Any(n => n.Name.Equals(tokens[i], StringComparison.OrdinalIgnoreCase)))
                    list.Add((tokens[i], type));
            }
        }

        // The game's own definition whose tags the baseline does not hold names nothing that can be shown.
        var known = effective.Tags.Length > 0 || entry.Origin != "game";
        return (named, known);
    }

    private string? RelationOf(ReferenceKind kind, string? type, string token)
    {
        return kind switch
        {
            ReferenceKind.ModelFile => index.ParticleSystems.Contains(ModelBoneKey.From(WithExtension(token, ".alo")))
                ? "particleSystems"
                : "models",
            ReferenceKind.TextureFile => "textures",
            ReferenceKind.AudioFile => "samples",
            ReferenceKind.WorkspaceFile => "luaScript",
            ReferenceKind.XmlObject => type switch
            {
                "Particle" => "particles",
                "SFXEvent" => "sfxEvents",
                "SpeechEvent" => "speechEvents",
                "HardPoint" => "hardpoints",
                _ => "otherReferences"
            },
            _ => null
        };
    }

    private SymbolRow RowOf(string relation, string name, string? type)
    {
        var found = relation switch
        {
            "models" => catalog.Find("models", WithExtension(name, ".alo")),
            "particleSystems" => catalog.Find("particleSystems", WithExtension(name, ".alo")),
            "textures" => Texture(name),
            "samples" => catalog.Find("audio", name),
            "luaScript" => catalog.Find("luaScripts", WithExtension(name, ".lua")),
            _ => catalog.FindObject(name)
        };
        return found ?? Missing(name, type);
    }

    /// <summary>
    ///     The texture a name means: the name as written, else the same name as a <c>.tga</c> or a
    ///     <c>.dds</c>, which the game takes for one texture - a model routinely names a <c>.tga</c>
    ///     that only ever ships as a <c>.dds</c>. The asset resolver's rule, so the browser and the
    ///     model preview agree on what is missing.
    /// </summary>
    private SymbolRow? Texture(string name)
    {
        if (catalog.Find("textures", name) is { } written) return written;
        var extension = Path.GetExtension(name);
        if (!s_textureInterchange.Contains(extension, StringComparer.OrdinalIgnoreCase)) return null;
        return s_textureInterchange
            .Where(e => !e.Equals(extension, StringComparison.OrdinalIgnoreCase))
            .Select(e => catalog.Find("textures", Path.ChangeExtension(name, e)))
            .FirstOrDefault(r => r is not null);
    }

    // A texture name as one key for its .tga and its .dds, in any case.
    private static string TextureKey(string name)
    {
        var lower = name.ToLowerInvariant();
        return s_textureInterchange.Contains(Path.GetExtension(lower)) ? Path.ChangeExtension(lower, ".tga") : lower;
    }

    private static SymbolRow Missing(string name, string? type)
    {
        return new SymbolRow(name, name, type, [], "missing", null, []);
    }

    private static string WithExtension(string name, string extension)
    {
        return Path.HasExtension(name) ? name : name + extension;
    }

    // The objects whose definitions name this one: each reference mapped to the object it sits in.
    private IEnumerable<string> ReferencingObjects(string id)
    {
        if (!index.WorkspaceReferences.TryGetValue(id, out var references)) return [];
        return references
            .Select(ContainingObject)
            .OfType<string>()
            .Where(o => !o.Equals(id, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
    }

    private string? ContainingObject(GameReference reference)
    {
        if (!index.Documents.TryGetValue(reference.DocumentUri, out var document)) return null;
        var id = document.Symbols
            .Where(s => s.Kind == GameSymbolKind.XmlObject && s.Origin is FileOrigin fo && fo.Line <= reference.Line)
            .MaxBy(s => ((FileOrigin)s.Origin).Line)?.Id;
        // An ability defined inside an object (OWNER$Ability) is indexed on its own; the reference is
        // the owner's.
        var owner = id?.IndexOf(GameIndex.OwnerScopeSeparator) ?? -1;
        return owner > 0 ? id![..owner] : id;
    }

    // Every layer's definition of the object, the one the game uses first, then the game's own.
    private List<SymbolRow> DefinitionRows(string id)
    {
        var rows = new List<SymbolRow>();
        if (index.WorkspaceDefinitions.TryGetValue(id, out var definitions))
            foreach (var definition in definitions
                         .Where(s => s.Kind == GameSymbolKind.XmlObject && s.Origin is FileOrigin)
                         .OrderByDescending(index.LayerRankOf))
            {
                var origin = (FileOrigin)definition.Origin;
                var layer = index.Documents.GetValueOrDefault(origin.Uri)?.LayerName;
                rows.Add(new SymbolRow(origin.Uri + "#" + origin.Line,
                    $"{origin.Uri[(origin.Uri.LastIndexOf('/') + 1)..]}:{origin.Line + 1}", definition.TypeName, [],
                    "layer:" + index.LayerRankOf(definition), layer, []));
            }

        if (index.Baseline.Symbols.TryGetValue(id, out var shipped) && shipped.Kind == GameSymbolKind.XmlObject)
            rows.Add(new SymbolRow(id, id, shipped.TypeName, [], "game", null, []));
        return rows;
    }
}