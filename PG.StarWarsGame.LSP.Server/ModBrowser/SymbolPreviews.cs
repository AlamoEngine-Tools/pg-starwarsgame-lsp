// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>
///     An entry's preview (#270): the tiles its type's adapter lists, each in its own cells of the
///     adapter's grid. The adapters live here rather than in the webview, so each is tested against
///     the index; the webview renders tile kinds and owns only the layout.
/// </summary>
/// <remarks>
///     <para>
///         A preview shows what the entry's file does not: its problems, its encyclopedia card, what
///         it names and what names it. Its definition is not one of them - the file, with its
///         highlighting, is one click away (maintainer, 2026-10-22).
///     </para>
///     <para>
///         A tile that is absent for one entry - Problems on a clean one - leaves its cells empty, so
///         the same tile is always in the same place; a tile a type never has is not in its layout. A
///         preview that would be its header alone is none at all: texture, audio, animation and enum
///         entries have no preview until they have something to show.
///     </para>
/// </remarks>
/// <param name="schema">The schema the Schema outline's entries document.</param>
/// <param name="locale">The language the server documents in, as the hovers do.</param>
public sealed class SymbolPreviews(
    GameIndex index,
    SymbolRelations relations,
    IDocumentTextSource texts,
    IXmlDiagnosticsCollector diagnostics,
    IFileSystem files,
    Func<string, int?>? bonesOf = null,
    ISchemaProvider? schema = null,
    string locale = "en")
{
    private const string GameObjects = "xml:GameObjectType";
    private const string ReferencedBy = "referencedBy";
    private const string UsedBy = "usedBy";

    /// <summary>The pages whose entries have a preview.</summary>
    private static readonly HashSet<string> s_assetPages = new(StringComparer.Ordinal)
    {
        "models", "particleSystems", "luaScripts", "schemaTypes", "schemaTags", "behaviours", "enums"
    };

    private static readonly GetSymbolPreviewResult s_none = new(null, []);

    /// <summary>Whether an entry of the page has a preview; the others only select.</summary>
    public static bool HasPreview(string page)
    {
        return page.StartsWith("xml:", StringComparison.Ordinal) || s_assetPages.Contains(page);
    }

    public GetSymbolPreviewResult Preview(string page, string id)
    {
        if (!HasPreview(page)) return s_none;
        var node = relations.Node(page, id);
        if (node.Entry is not { } entry) return s_none;

        var layout = new Layout();
        var adapter = page switch
        {
            GameObjects => GameObject(layout, page, entry, node),
            "models" => Model(layout, page, entry, node),
            "particleSystems" => ParticleSystem(layout, page, entry, node),
            "luaScripts" => LuaScript(layout, page, entry, node),
            "schemaTypes" => SchemaType(layout, page, entry, node),
            "schemaTags" => SchemaTag(layout, entry, node),
            "behaviours" => Behaviour(layout, page, entry, node),
            "enums" => Enum(layout, page, entry, node),
            _ => XmlObject(layout, page, entry, node)
        };
        return layout.Tiles.Count < 2
            ? s_none
            : new GetSymbolPreviewResult(adapter, layout.Tiles) { Columns = layout.Columns, Rows = layout.Rows };
    }

    /// <summary>
    ///     A game object: its header and problems across the top, its references under them, its
    ///     encyclopedia card down the right.
    /// </summary>
    private string GameObject(Layout layout, string page, SymbolRow entry, GetSymbolNodeResult node)
    {
        layout.Grid(6, 6);
        layout.Put(Header(entry, node), 0, 0, 2, 2);
        layout.Put(XmlProblems(node.Location), 2, 0, 2, 2);
        layout.Put(Encyclopedia(entry.Id, node), 4, 0, 2, 6);
        layout.Put(Entries(page, entry.Id, ReferencedBy), 0, 2, 4, 4);
        return "gameObject";
    }

    /// <summary>
    ///     An object of another type: its header and problems across the top, what its own tags name
    ///     under them - one tile per relation, sharing the rows - and its references down the right.
    /// </summary>
    private string XmlObject(Layout layout, string page, SymbolRow entry, GetSymbolNodeResult node)
    {
        layout.Grid(6, 6);
        layout.Put(Header(entry, node), 0, 0, 2, 2);
        layout.Put(XmlProblems(node.Location), 2, 0, 2, 2);
        var named = node.Relations
            .Where(r => r.Relation is not (ReferencedBy or "definitions") && r.Count > 0)
            .Select(r => r.Relation)
            .Take(4)
            .ToList();
        for (var i = 0; i < named.Count; i++)
            layout.Put(Entries(page, entry.Id, named[i]), 0, 2 + Share(4, named.Count, i), 4,
                Share(4, named.Count, i + 1) - Share(4, named.Count, i));
        layout.Put(Entries(page, entry.Id, ReferencedBy), 4, 0, 2, 6);
        return "xmlObject";
    }

    /// <summary>
    ///     A model, on a grid of four columns: its header with its counts and its problems, its textures
    ///     and particle systems, its animations and references, two to a band down the left half; its
    ///     tree of bones, meshes and effects down the right.
    /// </summary>
    /// <remarks>
    ///     Four columns at their least width fit beside the action column. The lists go two to a band,
    ///     each two rows tall, as a row keeps its height rather than stretching; the tree - the page's
    ///     main content, and as deep as the model's skeleton - takes the right half, every row tall.
    /// </remarks>
    private string Model(Layout layout, string page, SymbolRow entry, GetSymbolNodeResult node)
    {
        layout.Grid(4, 6);
        layout.Put(Header(entry, node) with { Facts = ModelFacts(entry, node) }, 0, 0, 1, 2);
        layout.Put(AssetProblems(page, entry.Id), 1, 0, 1, 2);
        // Its bones, meshes and effects as the model preview's tree, which the webview reads from
        // the model itself by name.
        layout.Put(Tile("hierarchy") with { ObjectId = entry.Name }, 2, 0, 2, 6);
        layout.Put(Entries(page, entry.Id, "textures"), 0, 2, 1, 2);
        layout.Put(Entries(page, entry.Id, "particleSystems"), 1, 2, 1, 2);
        layout.Put(Entries(page, entry.Id, "animations"), 0, 4, 1, 2);
        layout.Put(Entries(page, entry.Id, UsedBy), 1, 4, 1, 2);
        return "model";
    }

    /// <summary>A particle system: its header and problems on top, its textures under them, its references down the right.</summary>
    private string ParticleSystem(Layout layout, string page, SymbolRow entry, GetSymbolNodeResult node)
    {
        layout.Grid(6, 6);
        layout.Put(Header(entry, node), 0, 0, 2, 2);
        layout.Put(AssetProblems(page, entry.Id), 2, 0, 2, 2);
        layout.Put(Entries(page, entry.Id, "textures"), 0, 2, 4, 4);
        layout.Put(Entries(page, entry.Id, UsedBy), 4, 0, 2, 6);
        return "particleSystem";
    }

    /// <summary>A schema type: its header and its documentation on top, its tags under them.</summary>
    private string SchemaType(Layout layout, string page, SymbolRow entry, GetSymbolNodeResult node)
    {
        var type = schema?.AllObjectTypes.FirstOrDefault(t => t.TypeName == entry.Id);
        layout.Grid(6, 4);
        layout.Put(Header(entry, node), 0, 0, 2, 2);
        layout.Put(Documentation(
            type?.NameTag is { } nameTag ? [new PreviewProperty("nameTag", nameTag)] : [],
            type?.Description, type?.Notes), 2, 0, 4, 2);
        layout.Put(Entries(page, entry.Id, "tags"), 0, 2, 6, 2);
        return "schemaType";
    }

    /// <summary>
    ///     A tag: its header and its documentation - what it takes, what it references, whether it
    ///     repeats, its description and notes - and, for one that takes an enum, the enum's values.
    /// </summary>
    private string SchemaTag(Layout layout, SymbolRow entry, GetSymbolNodeResult node)
    {
        var slash = entry.Id.IndexOf('/');
        var typeName = entry.Id[..slash];
        var tag = schema?.GetTagsForType(typeName).FirstOrDefault(t => t.Tag == entry.Name);
        List<PreviewProperty> properties = [];
        if (tag is not null)
        {
            properties.Add(new PreviewProperty("valueType", tag.ValueType.ToString()));
            properties.Add(new PreviewProperty("type", typeName));
            // A tag that takes an enum references it; the enum's own row says so.
            if (tag.ReferenceKind != ReferenceKind.None && tag.Enum is null)
                properties.Add(new PreviewProperty("references",
                    tag.ReferenceTypeName ?? tag.ObjectType?.TypeName ?? tag.ReferenceKind.ToString()));
            if (tag.Enum is { } takes) properties.Add(new PreviewProperty("enum", takes.Name));
            if (tag.MultipleAllowed) properties.Add(new PreviewProperty("repeats", ""));
        }

        var values = tag?.Enum is { } enumeration ? EnumValueList.Of(index, enumeration) : [];
        layout.Grid(6, 2);
        layout.Put(Header(entry, node), 0, 0, 2, 2);
        layout.Put(Documentation(properties, tag?.Description, tag?.Notes), 2, 0, values.Count > 0 ? 2 : 4, 2);
        if (values.Count > 0)
            layout.Put(Tile("values") with
            {
                Count = values.Count,
                Entries = [.. values.Select(ValueEntry)]
            }, 4, 0, 2, 2);
        return "schemaTag";
    }

    /// <summary>
    ///     An enum: whether it is built in or read from the game's files, its documentation, every value
    ///     down the right, and the tags typed with it under them.
    /// </summary>
    private string Enum(Layout layout, string page, SymbolRow entry, GetSymbolNodeResult node)
    {
        var definition = schema?.AllEnums.FirstOrDefault(e => e.Name == entry.Id);
        List<PreviewProperty> properties = [];
        if (definition is not null)
        {
            properties.Add(new PreviewProperty("kind", EnumValueList.KindKey(definition.Kind)));
            if (definition.IsBitfield) properties.Add(new PreviewProperty("bitfield", ""));
        }

        var values = definition is null ? [] : EnumValueList.Of(index, definition);
        layout.Grid(6, 4);
        layout.Put(Header(entry, node), 0, 0, 2, 2);
        layout.Put(Documentation(properties, definition?.Description, definition?.Notes), 2, 0, 2, 2);
        layout.Put(Tile("values") with
        {
            Count = values.Count,
            Entries = [.. values.Select(ValueEntry)]
        }, 4, 0, 2, 4);
        layout.Put(Entries(page, entry.Id, UsedBy), 0, 2, 4, 2);
        return "enum";
    }

    // An enum value's line: its key, its value beside it where that is not the key, its description.
    private PreviewEntry ValueEntry(EnumValueList.Value value)
    {
        return new PreviewEntry(value.Name, Describe(value.Description))
        {
            Value = value.EngineValue.Equals(value.Name, StringComparison.Ordinal) ? null : value.EngineValue
        };
    }

    /// <summary>A behaviour: what decides it, and the game objects that have it.</summary>
    private string Behaviour(Layout layout, string page, SymbolRow entry, GetSymbolNodeResult node)
    {
        var kind = schema?.AllKinds.FirstOrDefault(k =>
            string.Equals(k.Kind, entry.Id, StringComparison.OrdinalIgnoreCase));
        List<PreviewProperty> properties = [];
        if (kind is not null)
        {
            if (kind.Behaviors.Count > 0)
                properties.Add(new PreviewProperty("behaviours", string.Join(", ", kind.Behaviors)));
            if (kind.Flags.Count > 0) properties.Add(new PreviewProperty("flags", string.Join(", ", kind.Flags)));
            if (kind.MemberOf.Count > 0)
                properties.Add(new PreviewProperty("memberOf", string.Join(", ", kind.MemberOf)));
        }

        layout.Grid(6, 4);
        layout.Put(Header(entry, node), 0, 0, 2, 2);
        layout.Put(Documentation(properties, kind?.Description, kind?.Notes), 2, 0, 4, 2);
        layout.Put(Entries(page, entry.Id, "objects"), 0, 2, 6, 2);
        return "behaviour";
    }

    // A schema element's documentation: its properties, its description and its notes, ranked as
    // the hovers rank them.
    private PreviewTile Documentation(IReadOnlyList<PreviewProperty> properties,
        IReadOnlyDictionary<string, string>? description, IReadOnlyList<SchemaNote>? notes)
    {
        return Tile("documentation") with
        {
            Properties = properties,
            Text = Describe(description),
            Notes =
            [
                .. SchemaNote.Ranked(notes ?? [])
                    .Select(n => new PreviewNote(n.Kind.ToString(), Describe(n.Text), n.Value))
            ]
        };
    }

    // A description in the language the server documents in, else English; none for an empty one.
    private string? Describe(IReadOnlyDictionary<string, string>? text)
    {
        return text?.GetValueOrDefault(locale) ?? text?.GetValueOrDefault("en");
    }

    /// <summary>A Lua script: its header, what loads it and what it requires, in one row.</summary>
    private string LuaScript(Layout layout, string page, SymbolRow entry, GetSymbolNodeResult node)
    {
        layout.Grid(6, 2);
        layout.Put(Header(entry, node), 0, 0, 2, 2);
        layout.Put(Entries(page, entry.Id, "loadedBy"), 2, 0, 2, 2);
        layout.Put(Entries(page, entry.Id, "requires"), 4, 0, 2, 2);
        return "luaScript";
    }

    private PreviewTile Header(SymbolRow entry, GetSymbolNodeResult node)
    {
        return Tile("identity") with
        {
            Entry = entry, Location = node.Location, VariantOf = node.VariantOf, File = FileOf(node)
        };
    }

    // The entry's file on disk as its header describes it: size and dates, a script's lines besides.
    // Nothing for one with no file, or one that has gone since it was indexed.
    private PreviewFile? FileOf(GetSymbolNodeResult node)
    {
        if (node.File is not { } path || !files.File.Exists(path)) return null;
        var info = files.FileInfo.New(path);
        var lines = node.Entry is { } entry && path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? texts.GetText(entry.Id)?.Text.Split('\n').Length
            : null;
        return new PreviewFile(info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc) { Lines = lines };
    }

    // A model's counts, for its header: its bones from the model file's own skeleton, and how many of
    // each relation it has. Not the model catalog's names, which a mesh named apart from any bone
    // adds to (measured on FoC: the Star Destroyer's 71 bones against 74 names); a model that cannot
    // be read has no bone count to give.
    private List<PreviewFact> ModelFacts(SymbolRow entry, GetSymbolNodeResult node)
    {
        int Counted(string relation)
        {
            return node.Relations.FirstOrDefault(r => r.Relation == relation)?.Count ?? 0;
        }

        List<PreviewFact> facts = bonesOf?.Invoke(entry.Id) is { } bones ? [new PreviewFact("bones", bones)] : [];
        facts.AddRange(
        [
            new PreviewFact("textures", Counted("textures")),
            new PreviewFact("particleSystems", Counted("particleSystems")),
            new PreviewFact("animations", Counted("animations"))
        ]);
        return facts;
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

    /// <summary>
    ///     The errors and warnings inside the definition the game uses; a hint is not a problem. None
    ///     for an object no text file of the project holds.
    /// </summary>
    private PreviewTile? XmlProblems(SymbolLocation? location)
    {
        if (location is null || texts.GetText(location.Uri) is not { } document) return null;
        var parsed = ParsedXmlDocument.Parse(document.Text);
        var span =
            parsed.Spans.Elements.FirstOrDefault(e => parsed.LineIndex.GetPosition(e.Start).Line == location.Line);
        if (span is null) return null;
        var last = parsed.LineIndex.GetPosition(span.End - 1).Line;

        var found = diagnostics.Collect(location.Uri, document.Text, index)
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(d => d.Range.Start.Line >= location.Line && d.Range.Start.Line <= last)
            .Select(d => new PreviewEntry(d.Message, d.Code?.String)
            {
                Location = new SymbolLocation(location.Uri, d.Range.Start.Line),
                Severity = d.Severity == DiagnosticSeverity.Error ? "error" : "warning"
            })
            .ToList();
        return found.Count == 0 ? null : Tile("problems") with { Count = found.Count, Entries = found };
    }

    // The card the encyclopedia action shows: there exactly when the action is offered, a planet's
    // and an object without body text left out alike.
    private static PreviewTile? Encyclopedia(string id, GetSymbolNodeResult node)
    {
        return node.Actions.Any(a => a is { Action: "encyclopedia", Reason: null })
            ? Tile("encyclopedia") with { ObjectId = id }
            : null;
    }

    // Every entry of the relation: the tile scrolls, so nothing is held back.
    private PreviewTile Entries(string page, string id, string relation)
    {
        var rows = relations.Rows(page, id, relation) ?? [];
        return Tile("entries") with
        {
            Relation = relation,
            Count = rows.Count,
            // An asset's type is its extension, which its name already ends with.
            Entries = rows
                .Select(r => new PreviewEntry(r.Name,
                    // A tag is said with its type: one tag name is a tag on several.
                    r.Page == "schemaTags"
                        ? r.Id[..r.Id.IndexOf('/')]
                        : r.Type is { } type && r.Name.EndsWith(type, StringComparison.OrdinalIgnoreCase)
                            ? null
                            : r.Type)
                {
                    Row = r
                })
                .ToList()
        };
    }

    // Where the i-th of n equal parts of a run of cells starts; part i ends where part i + 1 starts.
    private static int Share(int cells, int parts, int i)
    {
        return i * cells / parts;
    }

    // A tile of a kind, placed by Layout.Put.
    private static PreviewTile Tile(string kind)
    {
        return new PreviewTile(kind, 0, 0, 0, 0);
    }

    /// <summary>An adapter's grid and the tiles placed on it.</summary>
    private sealed class Layout
    {
        public List<PreviewTile> Tiles { get; } = [];
        public int Columns { get; private set; }
        public int Rows { get; private set; }

        public void Grid(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
        }

        // A tile in its own cells; one with nothing to show is left out, its cells left empty.
        public void Put(PreviewTile? tile, int x, int y, int width, int height)
        {
            if (tile is not null) Tiles.Add(tile with { X = x, Y = y, Width = width, Height = height });
        }
    }
}