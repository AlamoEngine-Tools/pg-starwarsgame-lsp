// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Persistence;

/// <summary>
///     Turns a document into the key persisted files name it by, and back again where the
///     candidates are known.
///     <para>
///         One central point on purpose: opening a document, saving a layout and migrating an old
///         sidecar all need the same answer, and a second place that folds a path for itself is a
///         second definition of "the same file". Everything asks here.
///     </para>
///     <para>
///         It lives in the server because that is where the workspace configuration is. The
///         intended home is beside the index and symbol repository, which is where every call site
///         that needs a path already goes; moving it there is its own change.
///     </para>
/// </summary>
public sealed class ProjectDocumentKeys(IModProjectReloadService reloadService, IFileHelper fileHelper)
{
    /// <summary>The root project's directory - the anchor every key is relative to.</summary>
    public string? ProjectDirectory
    {
        get
        {
            var rootLayer = reloadService.LastWorkspaceConfig?.Layers
                .OrderByDescending(l => l.Rank)
                .FirstOrDefault();
            if (rootLayer?.ProjectPath is not { } pgprojPath) return null;

            var normalized = pgprojPath.Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            return slash < 0 ? null : normalized[..slash];
        }
    }

    /// <summary>A graph's key: campaign and faction hashed together, like every other composite.</summary>
    /// <remarks>Static because it needs no workspace - the names come out of the campaign's own files.</remarks>
    public static Guid GraphKey(string campaign, string faction)
    {
        return DocumentKey.Composite(campaign, faction);
    }

    /// <summary>The key a sidecar written before layouts were faction-scoped used: the campaign alone.</summary>
    public static Guid LegacyGraphKey(string campaign)
    {
        return DocumentKey.Composite(campaign);
    }

    /// <summary>
    ///     Where a document sits inside the project, or null when there is no project or it lies
    ///     outside it - in which case it has no name that survives being opened on another machine.
    /// </summary>
    public string? RelativePathFor(string absolutePathOrUri)
    {
        return ProjectDirectory is { } root ? DocumentKey.Relative(root, absolutePathOrUri) : null;
    }

    /// <summary>One node's key: the thread it lives in and the event it is, hashed together.</summary>
    public Guid? NodeKey(string threadUri, string eventName)
    {
        return RelativePathFor(threadUri) is { } relative
            ? DocumentKey.Composite(relative, eventName)
            : null;
    }

    /// <summary>
    ///     The same key for a thread named only by its file name, as the layout sidecar named them
    ///     before this existed. Null when the name matches no file or more than one.
    /// </summary>
    /// <remarks>
    ///     Ambiguity is left unresolved rather than guessed: two threads of the same name in
    ///     different directories are exactly what a base name cannot tell apart, and picking one
    ///     would move a user's nodes onto the wrong graph.
    /// </remarks>
    public Guid? NodeKeyForBaseName(string baseName, string eventName)
    {
        return RelativePathForBaseName(baseName) is { } relative
            ? DocumentKey.Composite(relative, eventName)
            : null;
    }

    private string? RelativePathForBaseName(string baseName)
    {
        if (ProjectDirectory is null) return null;

        var roots = reloadService.LastWorkspaceConfig?.Layers
            .SelectMany(l => l.XmlDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var matches = new List<string>();
        foreach (var root in roots)
        {
            if (!fileHelper.FileSystem.Directory.Exists(root)) continue;
            matches.AddRange(fileHelper.FileSystem.Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => string.Equals(
                    fileHelper.FileSystem.Path.GetFileName(f), baseName, StringComparison.OrdinalIgnoreCase)));

            if (matches.Count > 1) return null;
        }

        return matches.Count == 1 ? RelativePathFor(matches[0]) : null;
    }
}
