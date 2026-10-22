// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>The Mod Browser's outlines (#270): the sidebar picks one, the panel lists its pages.</summary>
public static class SymbolOutlines
{
    public const string GameData = "gameData";
    public const string Assets = "assets";
    public const string Scripts = "scripts";
    public const string Schema = "schema";
}

/// <summary>An outline's pages with their counts, and the project's layers.</summary>
[Method("aet/getSymbolOutline", Direction.ClientToServer)]
public sealed record GetSymbolOutlineParams(string Outline) : IRequest<GetSymbolOutlineResult>;

/// <param name="Pages">The outline's pages, in the order the panel lists them.</param>
/// <param name="Layers">The project's layers, lowest first; an origin <c>layer:N</c> names one by rank.</param>
public sealed record GetSymbolOutlineResult(IReadOnlyList<SymbolPageSummary> Pages, IReadOnlyList<SymbolLayer> Layers);

/// <param name="Page">The page's key: <c>xml:&lt;type&gt;</c>, <c>models</c>, <c>enums</c> and the rest.</param>
/// <param name="Count">How many entries the page lists.</param>
public sealed record SymbolPageSummary(string Page, int Count);

public sealed record SymbolLayer(int Rank, string Name);

/// <summary>One page of entries, filtered and paged on the server: a large mod's set is too big to ship.</summary>
[Method("aet/getSymbolPage", Direction.ClientToServer)]
public sealed record GetSymbolPageParams(string Page) : IRequest<GetSymbolPageResult>
{
    /// <summary>A name to match, case-insensitively; names starting with it come first.</summary>
    public string? Query { get; init; }

    /// <summary>Kinds an entry must be one of; none filters nothing.</summary>
    public IReadOnlyList<string>? Kinds { get; init; }

    /// <summary>Origins an entry must come from (<c>game</c>, <c>project</c>, <c>layer:N</c>); none filters nothing.</summary>
    public IReadOnlyList<string>? Origins { get; init; }

    /// <summary>
    ///     The column the entries are sorted by - <c>name</c>, <c>type</c>, <c>kinds</c> or <c>origin</c> -
    ///     before the page is cut; none keeps the list's own order.
    /// </summary>
    public string? Sort { get; init; }

    public bool Descending { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = 200;

    /// <summary>When set, the page lists one relation of this entry rather than the page's own entries.</summary>
    public SymbolParent? Parent { get; init; }
}

/// <param name="Page">The page the entry is listed on.</param>
/// <param name="Id">The entry's id.</param>
/// <param name="Relation">The relation to list: <c>models</c>, <c>referencedBy</c>, <c>definitions</c> and the rest.</param>
public sealed record SymbolParent(string Page, string Id, string Relation);

/// <summary>One entry, opened: its row and what it relates to, each relation counted.</summary>
[Method("aet/getSymbolNode", Direction.ClientToServer)]
public sealed record GetSymbolNodeParams(string Page, string Id) : IRequest<GetSymbolNodeResult>;

/// <param name="Entry">The entry's row; null when the page lists no such entry.</param>
/// <param name="Relations">Its relations, in the order the panel shows them.</param>
public sealed record GetSymbolNodeResult(SymbolRow? Entry, IReadOnlyList<SymbolRelation> Relations)
{
    /// <summary>Where the definition the game uses is; null for one no text file of the project defines.</summary>
    public SymbolLocation? Location { get; init; }

    /// <summary>The object this one is a variant of, from <c>Variant_Of_Existing_Type</c>.</summary>
    public string? VariantOf { get; init; }

    /// <summary>What can be done with the entry - the code lens set - each with why when it cannot.</summary>
    public IReadOnlyList<SymbolAction> Actions { get; init; } = [];

    /// <summary>
    ///     Opening the entry into its preview: no reason where its type has a preview, <c>noPreview</c>
    ///     where its preview would be its header alone.
    /// </summary>
    public SymbolAction Open { get; init; } = new("open", null);

    /// <summary>The entry's file on disk, for an asset or a script that has one; null otherwise.</summary>
    public string? File { get; init; }
}

/// <param name="Uri">The defining document.</param>
/// <param name="Line">The zero-based line the definition starts on.</param>
public sealed record SymbolLocation(string Uri, int Line);

/// <param name="Action">
///     <c>definition</c>, <c>previewUnit</c>, <c>encyclopedia</c>, <c>effectiveObject</c> or
///     <c>previewModel</c>.
/// </param>
/// <param name="Reason">Why it is unavailable; null when it is available.</param>
public sealed record SymbolAction(string Action, string? Reason);

/// <summary>Asks for an entry's preview: the tiles its type's adapter shows (#270).</summary>
[Method("aet/getSymbolPreview", Direction.ClientToServer)]
public sealed record GetSymbolPreviewParams(string Page, string Id) : IRequest<GetSymbolPreviewResult>;

/// <param name="Adapter">
///     Which adapter answered - <c>gameObject</c>, <c>xmlObject</c>; null when the entry's type has
///     none, and a click on it only selects.
/// </param>
/// <param name="Tiles">The tiles, each placed on the grid by its adapter.</param>
public sealed record GetSymbolPreviewResult(string? Adapter, IReadOnlyList<PreviewTile> Tiles)
{
    /// <summary>The grid's columns; the webview stretches them across the width.</summary>
    public int Columns { get; init; }

    /// <summary>The grid's rows; the webview stretches them down the height.</summary>
    public int Rows { get; init; }
}

/// <summary>One tile of a preview, placed and sized in grid cells; which members it fills depends on its kind.</summary>
/// <param name="Kind">
///     <c>identity</c>, <c>problems</c>, <c>encyclopedia</c>, <c>definition</c> or <c>entries</c>.
/// </param>
/// <param name="X">Its first column, from 0.</param>
/// <param name="Y">Its first row, from 0.</param>
/// <param name="Width">Columns it spans.</param>
/// <param name="Height">Rows it spans.</param>
public sealed record PreviewTile(string Kind, int X, int Y, int Width, int Height)
{
    /// <summary>An entries tile's relation key, which also names the tile.</summary>
    public string? Relation { get; init; }

    /// <summary>How many entries the relation has; the tile may list fewer.</summary>
    public int? Count { get; init; }

    /// <summary>The identity tile's entry.</summary>
    public SymbolRow? Entry { get; init; }

    /// <summary>The identity tile's definition the game uses; null for one no text file of the project holds.</summary>
    public SymbolLocation? Location { get; init; }

    /// <summary>The identity tile's variant base.</summary>
    public string? VariantOf { get; init; }

    /// <summary>A problems or entries tile's lines.</summary>
    public IReadOnlyList<PreviewEntry> Entries { get; init; } = [];

    /// <summary>The encyclopedia tile's object.</summary>
    public string? ObjectId { get; init; }

    /// <summary>The identity tile's counts, in the order shown - a model's bones, textures and the rest.</summary>
    public IReadOnlyList<PreviewFact>? Facts { get; init; }

    /// <summary>The identity tile's file on disk, for an asset or a script that has one.</summary>
    public PreviewFile? File { get; init; }
}

/// <summary>A file on disk, as its header describes it.</summary>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Created">When it was created, in UTC.</param>
/// <param name="Modified">When it was last changed, in UTC.</param>
public sealed record PreviewFile(long Size, DateTime Created, DateTime Modified)
{
    /// <summary>A script's line count; null for any other file.</summary>
    public int? Lines { get; init; }
}

/// <param name="Key">What is counted: <c>bones</c>, <c>textures</c>, <c>particleSystems</c>, <c>animations</c>.</param>
/// <param name="Value">How many.</param>
public sealed record PreviewFact(string Key, int Value);

/// <param name="Name">What the line says: an entry's name, or a problem's message.</param>
/// <param name="Detail">A second, quieter line.</param>
public sealed record PreviewEntry(string Name, string? Detail)
{
    /// <summary>The entry the line opens; null for a problem.</summary>
    public SymbolRow? Row { get; init; }

    /// <summary>Where a problem is.</summary>
    public SymbolLocation? Location { get; init; }

    /// <summary>A problem's severity, <c>error</c> or <c>warning</c>, as the preview's problems carry it.</summary>
    public string? Severity { get; init; }

    /// <summary>
    ///     A problem the server found rather than a diagnostic: <c>missingTexture</c> or
    ///     <c>missingParticleSystem</c>, with <see cref="PreviewEntry.Name" /> the name no file holds.
    ///     The client words it.
    /// </summary>
    public string? Reason { get; init; }
}

/// <param name="Relation">The relation's key.</param>
/// <param name="Count">How many entries it lists; null until counted, for one that needs every object's tags.</param>
/// <param name="Reason">Why it lists none: <c>none</c>, or <c>notInModFiles</c> when the entry's tags are not known.</param>
public sealed record SymbolRelation(string Relation, int? Count, string? Reason);

/// <param name="Rows">The requested slice, sorted by name, prefix matches of the query first.</param>
/// <param name="Total">How many entries match before paging.</param>
/// <param name="Kinds">
///     Every kind a game object can be, with how many entries of it match the name and origin
///     filters - the kind filter left out, so picking one kind does not hide the others. None for a
///     list without game objects.
/// </param>
public sealed record GetSymbolPageResult(IReadOnlyList<SymbolRow> Rows, int Total)
{
    public IReadOnlyList<SymbolKindCount> Kinds { get; init; } = [];
}

/// <param name="Kind">The kind's name.</param>
/// <param name="Count">How many entries of the list are of it.</param>
public sealed record SymbolKindCount(string Kind, int Count);

/// <summary>One entry, as the definition the game uses.</summary>
/// <param name="Id">What the entry is looked up by: an object's name, an asset's path, a script's URI.</param>
/// <param name="Name">What the entry is listed as.</param>
/// <param name="Type">The XML type, the file extension, or nothing.</param>
/// <param name="Kinds">The kinds a game object is, from its behaviours and flags.</param>
/// <param name="Origin">Where the definition comes from: <c>game</c>, <c>project</c>, or <c>layer:N</c>.</param>
/// <param name="OriginName">The layer's name, for a <c>layer:N</c> origin.</param>
/// <param name="Overrides">The origins of the definitions it replaces, highest first.</param>
/// <param name="Page">The page the entry opens on; none for a missing name, a tag or a definition's place.</param>
public sealed record SymbolRow(
    string Id,
    string Name,
    string? Type,
    IReadOnlyList<string> Kinds,
    string Origin,
    string? OriginName,
    IReadOnlyList<string> Overrides,
    string? Page = null);