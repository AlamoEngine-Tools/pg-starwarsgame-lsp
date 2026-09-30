// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Workspace;

/// <inheritdoc cref="GetWatchDirectoriesParams" />
public sealed class GetWatchDirectoriesHandler(IProjectLayerMap layers)
    : IJsonRpcRequestHandler<GetWatchDirectoriesParams, GetWatchDirectoriesResult>
{
    public Task<GetWatchDirectoriesResult> Handle(
        GetWatchDirectoriesParams request, CancellationToken cancellationToken)
    {
        // Asset roots are deliberately absent: nothing in the watched glob set matches a texture,
        // a model or a sound, so watching them would cost OS handles over the largest directories
        // in a mod and deliver nothing. Add them here only together with a glob that wants them.
        var candidates = layers.Layers
            .SelectMany(layer => layer.XmlDirectories
                .Concat(layer.ScriptRoots)
                .Concat(layer.TextRoots)
                .Concat(layer.StoryDialogRoots)
                .Append(layer.ProjectDirectory))
            .OfType<string>()
            .Select(Normalize)
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(new GetWatchDirectoriesResult(Minimal(candidates)));
    }

    /// <summary>
    ///     The directories left once every one that another already contains is dropped. A watcher
    ///     costs an OS handle and each one is recursive, so a nested directory is pure overhead -
    ///     and a project directory usually swallows all of its own declared directories.
    /// </summary>
    private static List<string> Minimal(IReadOnlyList<string> candidates)
    {
        return candidates
            .Where(d => !candidates.Any(other => !ReferenceEquals(other, d) && Contains(other, d)))
            .ToList();
    }

    /// <summary>True when <paramref name="outer" /> is a strict ancestor of <paramref name="inner" />.</summary>
    private static bool Contains(string outer, string inner)
    {
        return inner.Length > outer.Length
               && inner.StartsWith(outer, StringComparison.OrdinalIgnoreCase)
               && inner[outer.Length] == '/';
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }
}
