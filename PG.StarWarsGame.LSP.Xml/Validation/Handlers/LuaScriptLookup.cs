// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Which file a Lua script name loads, the way the game finds it.
/// </summary>
/// <remarks>
///     Measured: the game loads a script by name through Lua's search path. The name is pasted into
///     every path template as written - no separator handling, no extension stripping - and the
///     templates are each script directory below, in this order, with <c>.lua</c> before
///     <c>.lc</c>. The first file that opens is the one loaded. Every template tries the mod
///     paths, the disk and the archives, so the merged catalog answers for all three at once.
/// </remarks>
public static class LuaScriptLookup
{
    /// <summary>Where a game object's script is expected to live.</summary>
    public const string GameObjectDirectory = "Data/Scripts/GameObject/";

    /// <summary>The script directories in the order the game searches them.</summary>
    public static readonly IReadOnlyList<string> SearchDirectories =
    [
        "Data/Scripts/Library/",
        GameObjectDirectory,
        "Data/Scripts/FreeStore/",
        "Data/Scripts/Miscellaneous/",
        "Data/Scripts/Story/",
        "Data/Scripts/AI/",
        "Data/Scripts/AI/SpaceMode/",
        "Data/Scripts/AI/LandMode/",
        "Data/Scripts/AI/StoryArc/",
        "Data/Scripts/Interventions/",
        "Data/Scripts/Evaluators/"
    ];

    private static readonly string[] Extensions = [".lua", ".lc"];

    /// <summary>
    ///     Every file <paramref name="name" /> would load, in the game's search order; the first is
    ///     the one it does load. Spelled as the game spells the path, with the name as written.
    /// </summary>
    public static IEnumerable<string> Candidates(IAssetFileIndex index, string name)
    {
        return SearchDirectories
            .SelectMany(_ => Extensions, (directory, extension) => directory + name + extension)
            .Where(path => index.Contains(path.ToLowerInvariant()));
    }

    /// <summary>Whether a resolved path is in the GameObject directory or below it.</summary>
    public static bool IsInGameObjectDirectory(string path)
    {
        return path.StartsWith(GameObjectDirectory, StringComparison.OrdinalIgnoreCase);
    }
}