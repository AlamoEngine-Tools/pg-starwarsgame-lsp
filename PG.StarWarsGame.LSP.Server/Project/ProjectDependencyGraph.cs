// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Project;

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
        Func<string, ModProjectFile?> loadReference)
    {
        var ordered = new List<(string Path, ModProjectFile File)>();
        var emitted = new HashSet<string>();
        var onStack = new HashSet<string>();

        Visit(Normalize(rootPath), root, loadReference, ordered, emitted, onStack);

        return ordered;
    }

    private void Visit(
        string normalizedPath,
        ModProjectFile file,
        Func<string, ModProjectFile?> loadReference,
        List<(string Path, ModProjectFile File)> ordered,
        HashSet<string> emitted,
        HashSet<string> onStack)
    {
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
                continue;

            var referenced = loadReference(resolved);
            if (referenced is null)
            {
                _logger.LogWarning(
                    "Project reference '{Reference}' resolved to '{Resolved}' could not be loaded; skipping.",
                    reference.Path, resolved);
                continue;
            }

            Visit(resolved, referenced, loadReference, ordered, emitted, onStack);
        }

        onStack.Remove(normalizedPath);

        if (emitted.Add(normalizedPath))
            ordered.Add((normalizedPath, file));
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }

    private static string GetDirectory(string normalizedPath)
    {
        var idx = normalizedPath.LastIndexOf('/');
        return idx < 0 ? string.Empty : normalizedPath[..idx];
    }

    private static string Combine(string directory, string relativeOrAbsolute)
    {
        var candidate = relativeOrAbsolute.Replace('\\', '/');
        if (IsRooted(candidate))
            return candidate;

        var basePath = string.IsNullOrEmpty(directory) ? "." : directory;
        var segments = new List<string>(basePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var basePrefix = basePath.StartsWith('/') ? "/" : string.Empty;

        foreach (var segment in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    if (segments.Count > 0)
                        segments.RemoveAt(segments.Count - 1);
                    break;
                default:
                    segments.Add(segment);
                    break;
            }

        return basePrefix + string.Join('/', segments);
    }

    private static bool IsRooted(string path)
    {
        if (path.StartsWith('/'))
            return true;
        return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
    }
}