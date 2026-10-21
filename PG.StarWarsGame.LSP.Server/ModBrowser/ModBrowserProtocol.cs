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

    public int Offset { get; init; }

    public int Limit { get; init; } = 200;
}

/// <param name="Rows">The requested slice, sorted by name, prefix matches of the query first.</param>
/// <param name="Total">How many entries match before paging.</param>
public sealed record GetSymbolPageResult(IReadOnlyList<SymbolRow> Rows, int Total);

/// <summary>One entry, as the definition the game uses.</summary>
/// <param name="Id">What the entry is looked up by: an object's name, an asset's path, a script's URI.</param>
/// <param name="Name">What the entry is listed as.</param>
/// <param name="Type">The XML type, the file extension, or nothing.</param>
/// <param name="Kinds">The kinds a game object is, from its behaviours and flags.</param>
/// <param name="Origin">Where the definition comes from: <c>game</c>, <c>project</c>, or <c>layer:N</c>.</param>
/// <param name="OriginName">The layer's name, for a <c>layer:N</c> origin.</param>
/// <param name="Overrides">The origins of the definitions it replaces, highest first.</param>
public sealed record SymbolRow(
    string Id,
    string Name,
    string? Type,
    IReadOnlyList<string> Kinds,
    string Origin,
    string? OriginName,
    IReadOnlyList<string> Overrides);