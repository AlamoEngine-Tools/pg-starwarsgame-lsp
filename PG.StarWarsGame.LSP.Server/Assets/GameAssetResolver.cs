// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Assets;

/// <summary>
///     Resolves a game-relative path the way the engine's virtual filesystem would.
/// </summary>
/// <remarks>
///     <para>
///         Precedence, highest first: the workspace's own asset roots (its layers in rank order), the
///         folder the game runs from, every archive the game loads, then - when the game runs from
///         the expansion - the base game folder. That is the game's measured lookup: mod folders, the
///         game folder, the archives, and the fallback folder last of all. Getting it wrong would make
///         a modder's own replacement asset silently invisible in the preview.
///     </para>
///     <para>
///         <strong>No install detection.</strong> The game directories come from configuration and
///         nowhere else. Probing for an install means reading the Windows registry, and this server is
///         meant to keep working on Linux.
///     </para>
///     <para>
///         Path matching is case-insensitive by explicit comparison rather than by relying on the
///         filesystem, for the same reason: the shipped data is mixed case, the XML referencing it is
///         inconsistent, and a case-sensitive filesystem would otherwise behave differently.
///     </para>
///     <para>
///         <see cref="Locate" /> opens the path exactly as written. A texture name's fallback to its
///         <c>.dds</c> is the texture lookup's, <see cref="IGameAssetResolver.LocateTexture" /> by
///         <see cref="GameTextureName.Candidates" /> (#420).
///     </para>
/// </remarks>
public sealed class GameAssetResolver(
    IFileHelper fileHelper,
    ILspConfigurationProvider config,
    IModProjectReloadService? projects,
    IMegArchiveSet archives,
    ILogger<GameAssetResolver> logger) : IGameAssetResolver
{
    /// <summary>
    ///     The directories a <c>.pgproj</c> can name as asset roots, as game-relative prefixes.
    /// </summary>
    private static readonly string[] AssetRootPrefixes = ["data/art/", "data/audio/"];

    public GameAssetTiers Tiers => new(
        WorkspaceRoots().Count,
        !string.IsNullOrWhiteSpace(config.Current.GamePath),
        !string.IsNullOrWhiteSpace(config.Current.ExpansionPath),
        archives.ArchiveCount);

    public GameAssetLocation? Locate(string gameRelativePath)
    {
        if (string.IsNullOrWhiteSpace(gameRelativePath))
            return null;

        var normalized = fileHelper.NormalizeGamePath(gameRelativePath);

        // Highest-precedence layer first; within a layer, the path as written and then its form inside
        // an art or audio root.
        foreach (var root in WorkspaceRoots())
        foreach (var relative in WorkspaceCandidates(normalized))
        {
            var hit = fileHelper.FindInWorkspace([root], relative);
            if (hit is not null)
                return new GameAssetLocation(gameRelativePath, hit, GameAssetTier.Workspace);
        }

        var (game, fallback) = GameDirectories();
        if (game is { } gameDirectory && fileHelper.FindInWorkspace([gameDirectory.Root], normalized) is { } inGame)
            return new GameAssetLocation(gameRelativePath, inGame, gameDirectory.Tier);

        if (archives.TryRead(normalized) is not null)
            return new GameAssetLocation(gameRelativePath, normalized, GameAssetTier.Archive);

        // The base game folder as the expansion's fallback: consulted only once the archives missed.
        if (fallback is { } fallbackDirectory
            && fileHelper.FindInWorkspace([fallbackDirectory.Root], normalized) is { } inFallback)
            return new GameAssetLocation(gameRelativePath, inFallback, fallbackDirectory.Tier);

        return null;
    }

    public byte[]? Read(string gameRelativePath)
    {
        var location = Locate(gameRelativePath);
        return location is null ? null : Read(location);
    }

    public byte[]? Read(GameAssetLocation location)
    {
        if (location.Tier == GameAssetTier.Archive)
            return archives.TryRead(location.ResolvedPath);

        try
        {
            return fileHelper.FileSystem.File.ReadAllBytes(location.ResolvedPath);
        }
        catch (Exception e)
        {
            // Located but unreadable - a lock, a permission, a file deleted between the two calls.
            // Worth a log rather than a throw: one unreadable asset must not abort a whole scene.
            logger.LogWarning(e, "Could not read asset {Path} at {Resolved}",
                location.RequestedPath, location.ResolvedPath);
            return null;
        }
    }

    /// <summary>
    ///     How one game-relative path may sit inside a workspace asset root.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A workspace asset root is not a game root. A <c>.pgproj</c> declares
    ///         <c>directories.art</c> (and <c>audio</c>), and <c>ModProjectResolver</c> turns those
    ///         into the asset roots - so a root is <c>&lt;project&gt;/data/art</c>, and the model
    ///         lives at <c>&lt;project&gt;/data/art/Models/foo.alo</c>. Looking for the whole
    ///         <c>Data/Art/Models/foo.alo</c> under it searches
    ///         <c>&lt;project&gt;/data/art/Data/Art/Models/foo.alo</c>, which never exists: every
    ///         asset in the modder's own workspace reports "not found" while sitting right there.
    ///     </para>
    ///     <para>
    ///         Both forms are tried, longest first, because a root MAY be a game directory - the
    ///         configured game paths use that shape, and a workspace is free to. The same shortening
    ///         already exists for <c>data/xml/</c> inside
    ///         <see cref="IFileHelper.FindInWorkspace" />, which is the same problem solved once for
    ///         the XML roots.
    ///     </para>
    /// </remarks>
    private static IEnumerable<string> WorkspaceCandidates(string normalized)
    {
        yield return normalized;

        foreach (var prefix in AssetRootPrefixes)
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return normalized[prefix.Length..];
                yield break;
            }
    }

    /// <summary>
    ///     The workspace's asset roots, highest-precedence layer first.
    /// </summary>
    /// <remarks>
    ///     Taken from the resolved layers when there are any, because their rank is the precedence the
    ///     rest of the server already agreed on. The flattened list is the fallback for a workspace
    ///     with no <c>.pgproj</c>, where there is no ranking to honour.
    /// </remarks>
    private List<string> WorkspaceRoots()
    {
        var workspace = projects?.LastWorkspaceConfig;
        if (workspace is null)
            return [];

        if (workspace.Layers.Count > 0)
            return
            [
                .. workspace.Layers
                    .OrderByDescending(l => l.Rank)
                    .SelectMany(l => l.AssetRoots)
            ];

        return [.. workspace.AssetRoots];
    }

    /// <summary>
    ///     The folder the game runs from, and its fallback folder.
    /// </summary>
    /// <remarks>
    ///     With an expansion configured the game runs from it and the base game folder is its
    ///     fallback - searched after the archives, not before them. With only the base game
    ///     configured, the base game is what runs and there is no fallback.
    /// </remarks>
    private ((string Root, GameAssetTier Tier)? Game, (string Root, GameAssetTier Tier)? Fallback) GameDirectories()
    {
        var expansion = config.Current.ExpansionPath;
        var baseGame = config.Current.GamePath;
        (string, GameAssetTier)? baseDirectory = string.IsNullOrWhiteSpace(baseGame)
            ? null
            : (baseGame, GameAssetTier.BaseGameLoose);

        return string.IsNullOrWhiteSpace(expansion)
            ? (baseDirectory, null)
            : ((expansion, GameAssetTier.ExpansionLoose), baseDirectory);
    }
}