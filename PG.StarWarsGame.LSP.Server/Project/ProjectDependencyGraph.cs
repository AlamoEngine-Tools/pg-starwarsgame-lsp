// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Project;

public sealed class ProjectDependencyGraph
{
    private readonly ILogger<ProjectDependencyGraph> _logger;

    public ProjectDependencyGraph(ILogger<ProjectDependencyGraph> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<(string Path, ModProjectFile File)> Build(
        string rootPath,
        ModProjectFile root,
        Func<string, ModProjectFile?> loadReference,
        out ProjectDependencyShape shape)
    {
        var ordered = new List<(string Path, ModProjectFile File)>();
        var emitted = new HashSet<string>();
        var onStack = new HashSet<string>();
        var tally = new Tally();

        Visit(Normalize(rootPath), root, loadReference, ordered, emitted, onStack, 0, tally);

        // The root is the last entry, and is not a dependency of itself.
        shape = new ProjectDependencyShape(tally.Direct, ordered.Count - 1, tally.Depth, tally.Unresolved);
        return ordered;
    }

    public IReadOnlyList<(string Path, ModProjectFile File)> Build(
        string rootPath,
        ModProjectFile root,
        Func<string, ModProjectFile?> loadReference)
    {
        return Build(rootPath, root, loadReference, out _);
    }

    private void Visit(
        string normalizedPath,
        ModProjectFile file,
        Func<string, ModProjectFile?> loadReference,
        List<(string Path, ModProjectFile File)> ordered,
        HashSet<string> emitted,
        HashSet<string> onStack,
        int depth,
        Tally tally)
    {
        tally.Depth = Math.Max(tally.Depth, depth);

        if (emitted.Contains(normalizedPath))
            return;

        if (!onStack.Add(normalizedPath))
        {
            _logger.LogWarning(
                "Cyclic project reference detected at '{Path}'; breaking the cycle.",
                normalizedPath);
            return;
        }

        var projectDir = GetDirectory(normalizedPath);
        foreach (var reference in file.ProjectReferences)
        {
            var resolved = Normalize(Combine(projectDir, reference.Path));
            if (emitted.Contains(resolved))
            {
                if (depth == 0) tally.Direct++;
                continue;
            }

            var referenced = loadReference(resolved);
            if (referenced is null)
            {
                _logger.LogWarning(
                    "Project reference '{Reference}' resolved to '{Resolved}' could not be loaded; skipping.",
                    reference.Path, resolved);
                tally.Unresolved++;
                continue;
            }

            if (depth == 0) tally.Direct++;
            Visit(resolved, referenced, loadReference, ordered, emitted, onStack, depth + 1, tally);
        }

        onStack.Remove(normalizedPath);

        if (emitted.Add(normalizedPath))
            ordered.Add((normalizedPath, file));
    }

    /// <summary>What the walk counts as it goes, for <see cref="ProjectDependencyShape" />.</summary>
    private sealed class Tally
    {
        public int Depth;
        public int Direct;
        public int Unresolved;
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }

    private static string GetDirectory(string normalizedPath)
    {
        return ProjectPathResolution.GetDirectory(normalizedPath);
    }

    /// <summary>
    ///     Resolves a dependency path, which this one may already state absolutely.
    /// </summary>
    /// <remarks>
    ///     An absolute path is returned untouched - there is nothing to resolve it against, and
    ///     walking it onto a base would corrupt it. <see cref="ModProjectResolver" /> has no such
    ///     branch because the references it follows are always relative.
    /// </remarks>
    private static string Combine(string directory, string relativeOrAbsolute)
    {
        var candidate = relativeOrAbsolute.Replace('\\', '/');
        return ProjectPathResolution.IsRooted(candidate)
            ? candidate
            : ProjectPathResolution.Resolve(directory, candidate);
    }
}