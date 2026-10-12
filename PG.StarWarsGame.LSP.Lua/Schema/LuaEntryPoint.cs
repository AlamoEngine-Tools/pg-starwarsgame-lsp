// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace PG.StarWarsGame.LSP.Lua.Schema;

/// <summary>Who calls an entry point by name.</summary>
public enum LuaEntryPointCaller
{
    /// <summary>The game itself.</summary>
    ENGINE,

    /// <summary>A vanilla library script, when the function is defined.</summary>
    LIBRARY
}

/// <summary>
///     A function a script defines for the game or a vanilla library to call by name, from the
///     schema's <c>lua/entrypoints.json</c>. Documentation only: the analyzer never sees it, so an
///     entry point is no declared global.
/// </summary>
/// <param name="Name">The global function name, as called.</param>
/// <param name="Caller">Who calls it.</param>
/// <param name="Libraries">For a library-called entry point, the modules calling it; empty otherwise.</param>
/// <param name="Kinds">The kinds of script it applies to (object, story, freestore, ...).</param>
/// <param name="Description">Plain text.</param>
public sealed record LuaEntryPoint(
    string Name,
    LuaEntryPointCaller Caller,
    IReadOnlyList<string> Libraries,
    IReadOnlyList<string> Kinds,
    string Description)
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) }
    };

    /// <summary>Parses an entry-point file; a malformed one fails loudly rather than half-loading.</summary>
    public static IReadOnlyList<LuaEntryPoint> ParseFile(string json)
    {
        var file = JsonSerializer.Deserialize<EntryPointFile>(json, s_options)
                   ?? throw new JsonException("The entry-point file is empty");
        return file.EntryPoints.Select(e => new LuaEntryPoint(
            e.Name ?? throw new JsonException("An entry point has no name"),
            e.Caller,
            e.Libraries ?? [],
            e.Kinds ?? [],
            e.Description ?? "")).ToList();
    }

    private sealed record EntryPointFile(List<EntryPointRow> EntryPoints);

    private sealed record EntryPointRow(
        string? Name,
        LuaEntryPointCaller Caller,
        List<string>? Libraries,
        List<string>? Kinds,
        string? Description);
}