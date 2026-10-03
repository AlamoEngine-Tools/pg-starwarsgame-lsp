// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Debug;

/// <summary>
///     Judges whether a project layer's directory can be handed to the game as a mod. The game
///     resolves a mod path by prefixing it to <c>Data\...</c> names and nothing else, so a project
///     is runnable exactly when every directory it declares sits under its own <c>Data</c> folder.
///     The project format deliberately does not enforce that; this check tells the author when
///     their layout needs an intermediate build step instead.
/// </summary>
public static class RunnableModLayout
{
    /// <summary>Returns the mod path, or null and the reason the layer cannot be launched as is.</summary>
    public static (string? ModPath, string? Reason) Judge(ProjectLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var projectDirectory = ProjectDirectoryOf(layer);
        if (projectDirectory is null)
            return (null, "The layer has no project file, so there is no directory to launch it from");

        if (projectDirectory.Contains(' '))
            return (null,
                $"The project directory '{projectDirectory}' contains a space; the game's command line cannot carry a path with one");

        var dataPrefix = projectDirectory + "/Data/";
        foreach (var directory in AllDirectories(layer))
        {
            if (!DocumentUris.StartsWith(directory + "/", dataPrefix))
                return (null,
                    $"'{directory}' is not under '{projectDirectory}/Data', and the game only reads a mod's Data tree");
        }

        return (projectDirectory, null);
    }

    public static string? ProjectDirectoryOf(ProjectLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (string.IsNullOrEmpty(layer.ProjectPath))
            return null;
        var normalized = layer.ProjectPath.Replace('\\', '/');
        var cut = normalized.LastIndexOf('/');
        return cut < 0 ? null : normalized[..cut];
    }

    private static IEnumerable<string> AllDirectories(ProjectLayer layer)
    {
        return layer.XmlDirectories
            .Concat(layer.ScriptRoots)
            .Concat(layer.TextRoots)
            .Concat(layer.AssetRoots)
            .Concat(layer.StoryDialogRoots)
            .Select(d => d.Replace('\\', '/').TrimEnd('/'));
    }
}