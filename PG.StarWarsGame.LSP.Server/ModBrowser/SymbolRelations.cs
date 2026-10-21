// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

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
public sealed class SymbolRelations(
    GameIndex index,
    ISchemaProvider schema,
    IVariantTagSource tags,
    SymbolCatalog catalog,
    Func<string, IReadOnlyList<string>>? proxiesOf = null)
{
    private const string GameObjects = "xml:GameObjectType";
    private const string ReferencedBy = "referencedBy";
    private const string Definitions = "definitions";
    private const string UsedBy = "usedBy";
    private static readonly char[] s_separators = [',', ' ', '\t', '\n', '\r'];

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
        ["enums"] = [UsedBy]
    };

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
            return new GetSymbolNodeResult(entry, (s_assetRelations.GetValueOrDefault(page) ?? [])
                .Select(r => NeedsEveryObject(page, r) && !s_users.TryGetValue(index, out _)
                    ? new SymbolRelation(r, null, null)
                    : Counted(r, AssetRows(page, entry, r).Count, "none"))
                .ToList());

        var (named, known) = TagRelations(entry);
        var keys = page == GameObjects
            ? s_objectRelations
            : named.Where(r => r.Value.Count > 0).Select(r => r.Key).ToArray();
        var relations = keys
            .Select(k => Counted(k, named.GetValueOrDefault(k)?.Count ?? 0, known ? "none" : "notInModFiles"))
            .Append(Counted(ReferencedBy, ReferencingObjects(entry.Id).Count(), "none"))
            .Append(Counted(Definitions, DefinitionRows(entry.Id).Count, "none"))
            .ToList();
        return new GetSymbolNodeResult(entry, relations);
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
                .Select(t => catalog.Find("textures", t) ?? Missing(t, null)),
            ("animations", "model") => ModelOf(entry.Name),
            ("enums", UsedBy) => schema.AllTags
                .Where(t => string.Equals(t.Enum?.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
                .Select(t => new SymbolRow(t.Tag, t.Tag, "tag", [], "schema", null, [])),
            ("luaScripts", "loadedBy") => ObjectsReferencing(WorkspaceFileKey.Create(WorkspaceFileKey.LuaScriptType,
                entry.Name)),
            ("luaScripts", "requires") => (index.Documents.GetValueOrDefault(entry.Id)?.RequireArgs ?? [])
                .Select(a => catalog.Find("luaScripts", WithExtension(a, ".lua")) ?? Missing(a, ".lua")),
            ("textures", UsedBy) => ModelsNaming(entry.Name).Concat(Users(entry.Name)),
            (_, UsedBy) => Users(entry.Name),
            _ => []
        };
        return rows.DistinctBy(r => r.Id).ToList();
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
        return index.ModelTextures
            .Where(m => m.Value.Contains(texture, StringComparer.OrdinalIgnoreCase))
            .Select(m => catalog.Find("models", m.Key) ?? catalog.Find("particleSystems", m.Key))
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
            "textures" => catalog.Find("textures", name),
            "samples" => catalog.Find("audio", name),
            "luaScript" => catalog.Find("luaScripts", WithExtension(name, ".lua")),
            _ => catalog.FindObject(name)
        };
        return found ?? Missing(name, type);
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