// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>
///     An entry's preview (#270): the tiles its type's adapter lists, in the order the grid places
///     them, each sized in cells. The adapters live here rather than in the webview, so each is
///     tested against the index; the webview renders tile kinds and owns only the layout.
/// </summary>
/// <remarks>
///     A tile with nothing to show is left out of the list rather than drawn empty: a clean entry
///     has no Problems tile, an object only the game defines has no Definition.
/// </remarks>
public sealed class SymbolPreviews(
    GameIndex index,
    ISchemaProvider schema,
    IVariantTagSource tags,
    SymbolRelations relations,
    IDocumentTextSource texts,
    IXmlDiagnosticsCollector diagnostics,
    ToolsFeatureFlags tools)
{
    private const string GameObjects = "xml:GameObjectType";
    private const string ReferencedBy = "referencedBy";
    private const string UsedBy = "usedBy";

    /// <summary>The grid every XML preview is laid out on; the webview stretches it over the tab.</summary>
    private const int Columns = 6;

    private const int Rows = 6;

    /// <summary>The column the lists stack down, on the right of every XML preview.</summary>
    private const int ListColumn = 4;

    /// <summary>How many entries a relation's tile lists; the rest is one click away in the rail.</summary>
    private const int ListedEntries = 20;

    private static readonly GetSymbolPreviewResult s_none = new(null, []);

    public GetSymbolPreviewResult Preview(string page, string id)
    {
        if (!page.StartsWith("xml:", StringComparison.Ordinal)) return AssetPreview(page, id);
        var node = relations.Node(page, id);
        if (node.Entry is not { } entry) return s_none;

        var gameObject = page == GameObjects;
        var element = ElementOf(node.Location);
        var identity = Tile("identity") with { Entry = entry, Location = node.Location, VariantOf = node.VariantOf };
        var problems = element is null ? null : Problems(element);
        var card = gameObject ? Encyclopedia(entry.Id) : null;
        var definition = element is null ? null : Definition(element, entry.Id, node.VariantOf);
        // An object of another type shows what its own tags name; a game object's are in the rail.
        var lists = (gameObject
                ? []
                : node.Relations
                    .Where(r => r.Relation is not (ReferencedBy or "definitions") && r.Count > 0)
                    .Select(r => Entries(page, entry.Id, r.Relation)))
            .Append(Entries(page, entry.Id, ReferencedBy))
            .ToList();
        return new GetSymbolPreviewResult(gameObject ? "gameObject" : "xmlObject",
            Place(gameObject, identity, problems, card, definition, lists))
        {
            Columns = Columns,
            Rows = Rows
        };
    }

    /// <summary>
    ///     Places the tiles in their fixed cells: the identity and the problems across the top left, the
    ///     card and the definition below them, the lists down the right. A tile that is not there leaves
    ///     its cells empty and no other tile grows into them - a fixed tile only ever takes its own space,
    ///     as in Scout (maintainer, 2026-10-22).
    /// </summary>
    /// <remarks>
    ///     The lists are the one region that divides itself: how many relations an object of another type
    ///     has varies from object to object, so its lists share the column's six rows between them.
    /// </remarks>
    private static List<PreviewTile> Place(bool gameObject, PreviewTile identity, PreviewTile? problems,
        PreviewTile? card, PreviewTile? definition, IReadOnlyList<PreviewTile> lists)
    {
        var tiles = new List<PreviewTile> { identity with { X = 0, Y = 0, Width = 2, Height = 2 } };
        if (problems is not null) tiles.Add(problems with { X = 2, Y = 0, Width = 2, Height = 2 });
        if (card is not null) tiles.Add(card with { X = 0, Y = 2, Width = 2, Height = 4 });
        if (definition is not null)
            // A game object's definition shares the rows with its card; another type has no card.
            tiles.Add(gameObject
                ? definition with { X = 2, Y = 2, Width = 2, Height = 4 }
                : definition with { X = 0, Y = 2, Width = 4, Height = 4 });

        var stacked = lists.Take(Rows).ToList();
        tiles.AddRange(stacked.Select((list, i) => list with
        {
            X = ListColumn, Y = Share(Rows, stacked.Count, i), Width = Columns - ListColumn,
            Height = Share(Rows, stacked.Count, i + 1) - Share(Rows, stacked.Count, i)
        }));
        return tiles;
    }

    /// <summary>
    ///     A model, a particle system, an animation or a Lua script, on the same grid as an XML object:
    ///     its identity and problems across the top left, its body below them, its lists down the right.
    ///     A texture, a sound and an enum have no adapter yet; a click on them only selects.
    /// </summary>
    /// <remarks>
    ///     An asset's lists are fixed, unlike an XML object's: a model's textures tile is there with
    ///     none in it, so the same relation is always in the same cells.
    /// </remarks>
    private GetSymbolPreviewResult AssetPreview(string page, string id)
    {
        if (page is not ("models" or "particleSystems" or "animations" or "luaScripts")) return s_none;
        var node = relations.Node(page, id);
        if (node.Entry is not { } entry) return s_none;

        var tiles = new List<PreviewTile>();

        // A tile in its own cells; one there is nothing to show in leaves them empty.
        void Put(PreviewTile? tile, int x, int y, int width, int height)
        {
            if (tile is not null) tiles.Add(tile with { X = x, Y = y, Width = width, Height = height });
        }

        Put(Tile("identity") with { Entry = entry, Location = node.Location }, 0, 0, 2, 2);
        switch (page)
        {
            case "models":
                Put(AssetProblems(page, entry.Id), 2, 0, 2, 2);
                Put(Facts(entry, node), 0, 2, 2, 4);
                Put(Entries(page, entry.Id, "textures"), 2, 2, 2, 4);
                tiles.AddRange(Stacked(page, entry.Id, ["particleSystems", "animations", UsedBy]));
                break;
            case "particleSystems":
                Put(AssetProblems(page, entry.Id), 2, 0, 2, 2);
                Put(Entries(page, entry.Id, "textures"), 0, 2, 4, 4);
                tiles.AddRange(Stacked(page, entry.Id, [UsedBy]));
                break;
            case "animations":
                Put(Entries(page, entry.Id, "model"), 2, 0, 2, 2);
                break;
            default:
                Put(Source(entry.Id), 0, 2, 4, 4);
                tiles.AddRange(Stacked(page, entry.Id, ["loadedBy", "requires"]));
                break;
        }

        var adapter = page switch
        {
            "models" => "model",
            "particleSystems" => "particleSystem",
            "animations" => "animation",
            _ => "luaScript"
        };
        return new GetSymbolPreviewResult(adapter, tiles) { Columns = Columns, Rows = Rows };
    }

    // The relations down the list column, sharing its rows.
    private IEnumerable<PreviewTile> Stacked(string page, string id, string[] relationKeys)
    {
        return relationKeys.Select((r, i) => Entries(page, id, r) with
        {
            X = ListColumn, Y = Share(Rows, relationKeys.Length, i), Width = Columns - ListColumn,
            Height = Share(Rows, relationKeys.Length, i + 1) - Share(Rows, relationKeys.Length, i)
        });
    }

    // What an asset names that no file holds: a texture, or a particle system a proxy names.
    private PreviewTile? AssetProblems(string page, string id)
    {
        IEnumerable<PreviewEntry> Missing(string relation, string reason)
        {
            // A relation the page does not have - a particle system's particle systems - is no rows.
            return (relations.Rows(page, id, relation) ?? [])
                .Where(r => r.Origin == "missing")
                .Select(r => new PreviewEntry(r.Name, null) { Severity = "warning", Reason = reason });
        }

        var found = Missing("textures", "missingTexture").Concat(Missing("particleSystems", "missingParticleSystem"))
            .ToList();
        return found.Count == 0 ? null : Tile("problems") with { Count = found.Count, Entries = found };
    }

    // A model's counts: its bones from the model catalog, and how many of each relation it has.
    private PreviewTile Facts(SymbolRow entry, GetSymbolNodeResult node)
    {
        int Counted(string relation)
        {
            return node.Relations.FirstOrDefault(r => r.Relation == relation)?.Count ?? 0;
        }

        var bones = index.ModelBones.GetValueOrDefault(ModelBoneKey.From(entry.Name), []).Length;
        return Tile("facts") with
        {
            Facts =
            [
                new PreviewFact("bones", bones), new PreviewFact("textures", Counted("textures")),
                new PreviewFact("particleSystems", Counted("particleSystems")),
                new PreviewFact("animations", Counted("animations"))
            ]
        };
    }

    /// <summary>
    ///     A script's source from its first line of code: the comment banner the game's own scripts
    ///     open with is skipped, line comments and block comments alike. Null for a script whose
    ///     text is not to hand.
    /// </summary>
    private PreviewTile? Source(string uri)
    {
        if (texts.GetText(uri) is not { } document) return null;
        var lines = document.Text.Replace("\r\n", "\n").Split('\n');
        var first = 0;
        var inBlock = false;
        for (; first < lines.Length; first++)
        {
            var line = lines[first].Trim();
            if (inBlock)
            {
                inBlock = !line.Contains("]]", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("--[[", StringComparison.Ordinal) || line.StartsWith("--[=", StringComparison.Ordinal))
            {
                inBlock = !line[4..].Contains("]]", StringComparison.Ordinal);
                continue;
            }

            if (line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal)) break;
        }

        return Tile("source") with { Text = string.Join("\n", lines[first..]), Line = first };
    }

    // Where the i-th of n equal parts of a run of cells starts; part i ends where part i + 1 starts.
    private static int Share(int cells, int parts, int i)
    {
        return i * cells / parts;
    }

    // A tile of a kind, placed later by Place.
    private static PreviewTile Tile(string kind)
    {
        return new PreviewTile(kind, 0, 0, 0, 0);
    }

    /// <summary>The definition the game uses, as its element in its file; null for one no file holds.</summary>
    private Element? ElementOf(SymbolLocation? location)
    {
        if (location is null || texts.GetText(location.Uri) is not { } document) return null;
        var parsed = ParsedXmlDocument.Parse(document.Text);
        var span =
            parsed.Spans.Elements.FirstOrDefault(e => parsed.LineIndex.GetPosition(e.Start).Line == location.Line);
        if (span is null) return null;
        return new Element(location.Uri, document.Text, document.Text[span.Start..span.End], location.Line,
            parsed.LineIndex.GetPosition(span.End - 1).Line);
    }

    // The errors and warnings inside the element; a hint is not a problem.
    private PreviewTile? Problems(Element element)
    {
        var found = diagnostics.Collect(element.Uri, element.Document, index)
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(d => d.Range.Start.Line >= element.StartLine && d.Range.Start.Line <= element.EndLine)
            .Select(d => new PreviewEntry(d.Message, d.Code?.String)
            {
                Location = new SymbolLocation(element.Uri, d.Range.Start.Line),
                Severity = d.Severity == DiagnosticSeverity.Error ? "error" : "warning"
            })
            .ToList();
        return found.Count == 0 ? null : Tile("problems") with { Count = found.Count, Entries = found };
    }

    private PreviewTile? Encyclopedia(string id)
    {
        return tools.Encyclopedia && index.Resolve(id) is { } symbol &&
               EncyclopediaTags.DefinesBody(index, tags, symbol)
            ? Tile("encyclopedia") with { ObjectId = id }
            : null;
    }

    private PreviewTile Definition(Element element, string id, string? variantOf)
    {
        string? effective = null;
        if (tools.Variants && !string.IsNullOrEmpty(variantOf))
        {
            var resolved = new EffectiveObjectResolver(index, schema, tags).Resolve(id);
            if (resolved.Found) effective = EffectiveObjectXmlRenderer.Render(resolved);
        }

        return Tile("definition") with { Text = element.Text, Effective = effective };
    }

    private PreviewTile Entries(string page, string id, string relation)
    {
        var rows = relations.Rows(page, id, relation) ?? [];
        return Tile("entries") with
        {
            Relation = relation,
            Count = rows.Count,
            // An asset's type is its extension, which its name already ends with.
            Entries = rows.Take(ListedEntries)
                .Select(r => new PreviewEntry(r.Name,
                    r.Type is { } type && r.Name.EndsWith(type, StringComparison.OrdinalIgnoreCase) ? null : r.Type)
                {
                    Row = r
                })
                .ToList()
        };
    }

    /// <param name="Document">The whole file, which the diagnostics are collected over.</param>
    /// <param name="Text">The element alone, as written.</param>
    private sealed record Element(string Uri, string Document, string Text, int StartLine, int EndLine);
}