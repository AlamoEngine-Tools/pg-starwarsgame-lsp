// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     The directories a server scans at startup.
///     <para>
///         The client sends every folder of the window as a workspace folder, whichever project the
///         server is for. A server given its project scans that project's root and nothing else
///         (#167): with two projects open, the window's folders put one project's metafiles into
///         the other's pre-scan, and the first folder - another project's - became the root the
///         icon catalog and the encyclopedia read from.
///     </para>
/// </summary>
public static class ScanRoots
{
    /// <param name="workspaceFolders">The window's folders, as file-system paths.</param>
    /// <param name="rootPath">The legacy root URI's path, used when no folders were sent.</param>
    /// <param name="configWorkspaceRoot">The root the client configured for this server.</param>
    /// <param name="hasProjectPath">Whether the client named this server's project file.</param>
    public static IReadOnlyList<string> Compute(
        IReadOnlyList<string> workspaceFolders,
        string? rootPath,
        string? configWorkspaceRoot,
        bool hasProjectPath)
    {
        if (hasProjectPath && !string.IsNullOrEmpty(configWorkspaceRoot))
            return [configWorkspaceRoot];

        var folders = new List<string>();
        if (workspaceFolders.Count > 0)
            folders.AddRange(workspaceFolders);
        else if (!string.IsNullOrEmpty(rootPath))
            folders.Add(rootPath);

        if (configWorkspaceRoot is not null &&
            !folders.Any(f => string.Equals(f, configWorkspaceRoot, StringComparison.OrdinalIgnoreCase)))
            folders.Add(configWorkspaceRoot);

        return folders;
    }
}