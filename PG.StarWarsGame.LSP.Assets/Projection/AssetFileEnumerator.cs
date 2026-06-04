// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Enumerates loose game-asset files (textures, models, audio, maps) on disk under a game
///     repository root and returns their paths normalised relative to that root (lowercase,
///     forward-slash). Used by the BaselineBuilder to populate
///     <see cref="Core.Symbols.BaselineIndex.AssetFiles" />.
/// </summary>
/// <remarks>
///     The engine's <c>IGameRepository</c> only exposes <c>OpenFile</c>/<c>FileExists</c>, with no
///     listing API, so assets packed inside .meg archives cannot be enumerated here — only loose
///     files on disk under the repository root are collected. This is a known coverage gap for
///     fully MEG-packed installs.
/// </remarks>
public static class AssetFileEnumerator
{
    private static readonly ImmutableHashSet<string> AssetExtensions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            ".tga", ".dds", ".alo", ".wav", ".mp3", ".ted");

    public static ImmutableHashSet<string> Enumerate(IFileSystem fileSystem, string rootPath)
    {
        if (string.IsNullOrEmpty(rootPath) || !fileSystem.Directory.Exists(rootPath))
            return ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        var root = fileSystem.Path.GetFullPath(rootPath);

        foreach (var file in fileSystem.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var ext = fileSystem.Path.GetExtension(file);
            if (!AssetExtensions.Contains(ext))
                continue;

            var relative = fileSystem.Path.GetRelativePath(root, file);
            var normalized = Normalize(relative);
            if (!normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                continue;
            builder.Add(normalized);
        }

        return builder.ToImmutable();
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
}
