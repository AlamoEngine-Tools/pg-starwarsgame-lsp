// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>
///     Everything the Mod Browser lists (#270), by page: every indexed entry once, as the definition
///     the game uses - the highest layer's, else the game's own.
/// </summary>
/// <remarks>
///     Built once per <see cref="GameIndex" /> instance and kept beside it: an index never changes,
///     a change is a new index. Kinds rest on behaviours and flags the index already holds, so no
///     row reads a file. Relations are not here; they are computed for one entry when it is opened.
/// </remarks>
public sealed class SymbolCatalog
{
    private const string GameObjectType = "GameObjectType";
    private const int MostPerPage = 1000;

    private static readonly ConditionalWeakTable<GameIndex, SymbolCatalog> s_built = new();

    private static readonly string[] s_textureExtensions = [".tga", ".dds"];
    private static readonly string[] s_audioExtensions = [".wav", ".mp3"];

    private readonly Dictionary<string, IReadOnlyList<string>> _outlines;
    private readonly Dictionary<string, List<SymbolRow>> _pages;
    private readonly Dictionary<string, SymbolRow> _xmlById;
    private readonly Dictionary<string, Dictionary<string, SymbolRow>> _byName;
    private readonly IReadOnlyList<string> _kinds;

    private SymbolCatalog(Dictionary<string, List<SymbolRow>> pages, Dictionary<string, IReadOnlyList<string>> outlines,
        IReadOnlyList<string> kinds)
    {
        _pages = pages;
        _outlines = outlines;
        _kinds = kinds;
        _xmlById = new Dictionary<string, SymbolRow>(StringComparer.OrdinalIgnoreCase);
        _byName = new Dictionary<string, Dictionary<string, SymbolRow>>(StringComparer.Ordinal);
        foreach (var (page, rows) in pages)
        {
            var byName = _byName[page] = new Dictionary<string, SymbolRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                byName.TryAdd(row.Name, row);
                if (page.StartsWith("xml:", StringComparison.Ordinal)) _xmlById.TryAdd(row.Id, row);
            }
        }
    }

    /// <summary>The catalog of an index, built on first use and kept as long as the index is.</summary>
    public static SymbolCatalog For(GameIndex index, IReadOnlyList<ObjectKindDefinition> kinds,
        IReadOnlyList<EnumDefinition> enums)
    {
        return s_built.GetValue(index, i => Build(i, kinds, enums));
    }

    public static SymbolCatalog Build(GameIndex index, IReadOnlyList<ObjectKindDefinition> kinds,
        IReadOnlyList<EnumDefinition> enums)
    {
        var pages = new Dictionary<string, List<SymbolRow>>(StringComparer.Ordinal);

        foreach (var row in XmlRows(index, kinds))
            Add(pages, "xml:" + row.Type, row);
        var xmlPages = pages.Keys
            .OrderBy(p => p == "xml:" + GameObjectType ? 0 : 1)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string[] assetPages = ["models", "particleSystems", "animations", "textures", "audio", "luaScripts"];
        foreach (var page in assetPages) pages[page] = [];
        foreach (var (page, row) in AssetRows(index)) Add(pages, page, row);
        foreach (var row in ScriptRows(index)) Add(pages, "luaScripts", row);

        pages["enums"] = enums
            .Select(e => new SymbolRow(e.Name, e.Name, null, [], "game", null, [], "enums"))
            .ToList();

        foreach (var rows in pages.Values)
            rows.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

        return new SymbolCatalog(pages, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [SymbolOutlines.GameData] = xmlPages,
            [SymbolOutlines.Assets] = assetPages,
            [SymbolOutlines.Schema] = ["enums"]
        }, kinds.Select(k => k.Kind).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>An outline's pages with their counts; none for an outline that does not exist.</summary>
    public IReadOnlyList<SymbolPageSummary> Outline(string outline)
    {
        return _outlines.TryGetValue(outline, out var pages)
            ? pages.Select(p => new SymbolPageSummary(p, _pages[p].Count)).ToList()
            : [];
    }

    /// <summary>One page, filtered and paged; empty for a page that does not exist.</summary>
    public GetSymbolPageResult Page(GetSymbolPageParams request)
    {
        if (!_pages.TryGetValue(request.Page, out var rows)) return new GetSymbolPageResult([], 0);
        return Filter(rows, request);
    }

    /// <summary>The row an entry of a page is listed as, by its id or name; null when there is none.</summary>
    public SymbolRow? Find(string page, string idOrName)
    {
        if (page.StartsWith("xml:", StringComparison.Ordinal))
            return _xmlById.GetValueOrDefault(idOrName);
        return _byName.TryGetValue(page, out var byName)
            ? byName.GetValueOrDefault(idOrName) ?? _pages[page].FirstOrDefault(r => r.Id == idOrName)
            : null;
    }

    /// <summary>The row of an XML object, on whatever page its type puts it; null when none defines it.</summary>
    public SymbolRow? FindObject(string id)
    {
        return _xmlById.GetValueOrDefault(id);
    }

    /// <summary>Every row of a page, sorted by name; none for a page that does not exist.</summary>
    public IReadOnlyList<SymbolRow> Rows(string page)
    {
        return _pages.GetValueOrDefault(page) ?? [];
    }

    /// <summary>Every XML object's row, on every page.</summary>
    public IEnumerable<SymbolRow> Objects => _xmlById.Values;

    /// <summary>
    ///     Rows filtered by the request's query, kinds and origins, and paged; with every kind counted
    ///     over the rows the query and origins leave, when the list holds a game object.
    /// </summary>
    public GetSymbolPageResult Filter(IReadOnlyList<SymbolRow> rows, GetSymbolPageParams request)
    {
        IEnumerable<SymbolRow> matching = rows;
        if (request.Origins is { Count: > 0 } origins)
            matching = matching.Where(r => origins.Contains(r.Origin, StringComparer.Ordinal));
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var query = request.Query.Trim();
            matching = matching
                .Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        }

        var unkinded = matching.ToList();
        var filtered = request.Kinds is { Count: > 0 } kinds
            ? [.. unkinded.Where(r => r.Kinds.Any(k => kinds.Contains(k, StringComparer.OrdinalIgnoreCase)))]
            : unkinded;
        if (request.Sort is { } column) filtered = Sorted(filtered, column, request.Descending);
        var slice = filtered
            .Skip(Math.Max(0, request.Offset))
            .Take(Math.Clamp(request.Limit, 1, MostPerPage))
            .ToList();
        return new GetSymbolPageResult(slice, filtered.Count) { Kinds = KindCounts(rows, unkinded) };
    }

    // A column's order, ties by name. Origins run from the project's own layer down to the game's -
    // the leafiest layer first - and a name nothing defines last; an unknown column keeps the list's.
    private static List<SymbolRow> Sorted(List<SymbolRow> rows, string column, bool descending)
    {
        Func<SymbolRow, IComparable>? key = column switch
        {
            "name" => r => r.Name.ToLowerInvariant(),
            "type" => r => (r.Type ?? string.Empty).ToLowerInvariant(),
            "kinds" => r => string.Join(",", r.Kinds).ToLowerInvariant(),
            "origin" => r => OriginRank(r.Origin),
            _ => null
        };
        if (key is null) return rows;
        var ordered = descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
        return [.. ordered.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static int OriginRank(string origin)
    {
        return origin switch
        {
            "project" => int.MinValue,
            "game" => int.MaxValue - 1,
            "missing" => int.MaxValue,
            _ when origin.StartsWith("layer:", StringComparison.Ordinal) &&
                   int.TryParse(origin.AsSpan("layer:".Length), out var rank) => -rank,
            _ => int.MaxValue - 2
        };
    }

    private IReadOnlyList<SymbolKindCount> KindCounts(IReadOnlyList<SymbolRow> rows, List<SymbolRow> matching)
    {
        if (!rows.Any(r => string.Equals(r.Type, GameObjectType, StringComparison.OrdinalIgnoreCase))) return [];
        var counts = matching
            .SelectMany(r => r.Kinds)
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return [.. _kinds.Select(k => new SymbolKindCount(k, counts.GetValueOrDefault(k)))];
    }

    private static void Add(Dictionary<string, List<SymbolRow>> pages, string page, SymbolRow row)
    {
        if (!pages.TryGetValue(page, out var rows)) pages[page] = rows = [];
        rows.Add(row with { Page = page });
    }

    // One row per object name: the highest layer's definition, else the game's.
    private static IEnumerable<SymbolRow> XmlRows(GameIndex index, IReadOnlyList<ObjectKindDefinition> kinds)
    {
        var ids = new HashSet<string>(index.WorkspaceDefinitions.Keys, StringComparer.Ordinal);
        ids.UnionWith(index.Baseline.Symbols.Keys);

        foreach (var id in ids)
        {
            // An ability's own name inside an object (MY_UNIT$Medic_Healing) is not an entry of its own.
            if (id.Contains(GameIndex.OwnerScopeSeparator)) continue;

            var workspace = index.WorkspaceDefinitions.TryGetValue(id, out var definitions)
                ? definitions.Where(s => s.Kind == GameSymbolKind.XmlObject && s.Origin is FileOrigin)
                    .OrderByDescending(index.LayerRankOf)
                    .ToList()
                : [];
            var shipped = index.Baseline.Symbols.TryGetValue(id, out var s) && s.Kind == GameSymbolKind.XmlObject
                ? s
                : null;
            var winner = workspace.FirstOrDefault() ?? shipped;
            if (winner?.TypeName is not { } type) continue;

            var origins = workspace.Select(d => OriginOf(index, d)).Distinct().ToList();
            if (shipped is not null) origins.Add(("game", null));

            yield return new SymbolRow(id, id, type, KindsOf(index, winner, kinds), origins[0].Key, origins[0].Name,
                origins.Skip(1).Select(o => o.Key).ToList());
        }
    }

    private static (string Key, string? Name) OriginOf(GameIndex index, GameSymbol symbol)
    {
        var uri = ((FileOrigin)symbol.Origin).Uri;
        var document = index.Documents.GetValueOrDefault(uri);
        return ("layer:" + index.LayerRankOf(symbol), document?.LayerName);
    }

    private static IReadOnlyList<string> KindsOf(GameIndex index, GameSymbol symbol,
        IReadOnlyList<ObjectKindDefinition> kinds)
    {
        if (!string.Equals(symbol.TypeName, GameObjectType, StringComparison.OrdinalIgnoreCase)) return [];
        return kinds.Where(k => ObjectKinds.Match(index, symbol, k) == KindMatch.Yes).Select(k => k.Kind).ToList();
    }

    // One row per file name: the game resolves an asset by name, so a second row would open the same
    // file. The project's copy is the one kept, as the scene resolves to it.
    private static IEnumerable<(string Page, SymbolRow Row)> AssetRows(GameIndex index)
    {
        var assets = index.AssetFiles;
        IEnumerable<(string Page, string Path)> files =
        [
            .. assets.GetByExtension(".alo").Select(p =>
                (index.ParticleSystems.Contains(ModelBoneKey.From(p)) ? "particleSystems" : "models", p)),
            .. assets.GetByExtension(".ala").Select(p => ("animations", p)),
            .. s_textureExtensions.SelectMany(assets.GetByExtension).Select(p => ("textures", p)),
            .. s_audioExtensions.SelectMany(assets.GetByExtension).Select(p => ("audio", p))
        ];

        return files
            .Select(f => (f.Page, Name: MergedAssetFileIndex.FileNameOf(f.Path), f.Path,
                Shipped: assets.IsPackedAsset(f.Path)))
            .OrderBy(f => f.Shipped)
            .DistinctBy(f => (f.Page, f.Name.ToLowerInvariant()))
            .Select(f => (f.Page, new SymbolRow(f.Path, f.Name, Path.GetExtension(f.Name).ToLowerInvariant(), [],
                f.Shipped ? "game" : "project", null, [])));
    }

    private static IEnumerable<SymbolRow> ScriptRows(GameIndex index)
    {
        return index.Documents.Values
            .Where(d => d.DocumentUri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            .Select(d => new SymbolRow(d.DocumentUri, d.DocumentUri[(d.DocumentUri.LastIndexOf('/') + 1)..], ".lua",
                [], "layer:" + d.LayerRank, d.LayerName, []));
    }
}