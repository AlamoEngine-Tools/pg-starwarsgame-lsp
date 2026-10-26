// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     What a folder is to the game, read the way the game reads it - every name in any case - and
///     read once per folder. New Mod Project and Adopt Project decide on the same answers, as the
///     client's offer does (<c>modLayout.ts</c>); before this each handler carried its own rule and
///     the two disagreed on which project files and which folders to refuse (#317).
/// </summary>
public sealed class ModLayout(IFileHelper fileHelper)
{
    /// <summary>The file a mod describes itself in.</summary>
    public const string ModinfoFileName = "modinfo.json";

    /// <summary>The folders under Data the game reads; a mod holds at least one of them.</summary>
    public static readonly string[] DataFolders = ["XML", "Scripts", "Art", "Audio", "Text"];

    /// <summary>The executables the game runs from; the folder holding one is the game, not a mod.</summary>
    public static readonly string[] GameExecutables = ["sweaw.exe", "swfoc.exe", "starwarsg.exe", "starwarsi.exe"];

    /// <summary>Reads the folder once. A folder that does not exist is nothing: no project, no mod, not the game.</summary>
    public ModFolder Inspect(string root)
    {
        var fs = fileHelper.FileSystem;
        if (!fs.Directory.Exists(root))
            return new ModFolder(root, null, false, null, false, null, []);

        var files = fs.Directory.GetFiles(root);
        var projectFile = files.FirstOrDefault(f =>
            fs.Path.GetExtension(f).Equals(".pgproj", StringComparison.OrdinalIgnoreCase));
        var isGame = files.Any(f =>
            GameExecutables.Contains(fs.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
        var modinfo = files.FirstOrDefault(f =>
            fs.Path.GetFileName(f).Equals(ModinfoFileName, StringComparison.OrdinalIgnoreCase));

        var data = fs.Directory.GetDirectories(root).FirstOrDefault(d => Named(fs, d, "Data"));
        var dataChildren = data is null ? [] : fs.Directory.GetDirectories(data);
        var holdsModData = data is not null &&
                           (dataChildren.Any(d => DataFolders.Any(name => Named(fs, d, name))) ||
                            fs.Directory.GetFiles(data).Any(f =>
                                fs.Path.GetExtension(f).Equals(".meg", StringComparison.OrdinalIgnoreCase)));

        return new ModFolder(root, projectFile, isGame, data, holdsModData, modinfo, dataChildren);
    }

    private static bool Named(System.IO.Abstractions.IFileSystem fs, string directory, string name)
    {
        return string.Equals(fs.Path.GetFileName(directory), name, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>One reading of a folder; every path is spelled as it is on disk.</summary>
/// <param name="Root">The folder asked about.</param>
/// <param name="ProjectFile">The first project file in the folder, whatever its name, or null.</param>
/// <param name="IsGame">Whether the folder holds one of the game's executables.</param>
/// <param name="Data">The folder's Data directory, or null.</param>
/// <param name="HoldsModData">Whether Data holds one of the game's folders or a MEG archive.</param>
/// <param name="Modinfo">The folder's modinfo.json, or null.</param>
/// <param name="DataChildren">The directories under Data, read once.</param>
public sealed record ModFolder(
    string Root,
    string? ProjectFile,
    bool IsGame,
    string? Data,
    bool HoldsModData,
    string? Modinfo,
    IReadOnlyList<string> DataChildren)
{
    /// <summary>Whether the folder is a mod the game would read: mod data under Data, or a modinfo.json.</summary>
    public bool IsMod => HoldsModData || Modinfo is not null;

    /// <summary>A directory under Data by name in any case, spelled as it is on disk, or null.</summary>
    public string? DataChild(string name)
    {
        return DataChildren.FirstOrDefault(d =>
            string.Equals(Path.GetFileName(d.TrimEnd('/', '\\')), name, StringComparison.OrdinalIgnoreCase));
    }
}
