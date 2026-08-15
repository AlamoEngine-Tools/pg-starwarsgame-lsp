// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Assets;

/// <summary>Which layer of the virtual filesystem supplied an asset.</summary>
public enum GameAssetTier
{
    /// <summary>A loose file under one of the workspace's own asset roots.</summary>
    Workspace,

    /// <summary>A loose file under the configured expansion (Forces of Corruption) directory.</summary>
    ExpansionLoose,

    /// <summary>A loose file under the configured base game (Empire at War) directory.</summary>
    BaseGameLoose,

    /// <summary>An entry inside one of the configured directories' MEG archives.</summary>
    Archive
}

/// <summary>Where an asset was found.</summary>
/// <param name="ResolvedPath">
///     The absolute file path, or the archive-relative entry path for <see cref="GameAssetTier.Archive" />.
/// </param>
public sealed record GameAssetLocation(
    string RequestedPath, string ResolvedPath, GameAssetTier Tier);

/// <summary>
///     Which tiers this workspace can currently resolve from.
/// </summary>
/// <remarks>
///     Reported rather than inferred so the preview can explain a miss. "This model is not in your
///     mod and you have not told the extension where the game is" is actionable; "model not found"
///     is not.
/// </remarks>
public sealed record GameAssetTiers(
    int WorkspaceRootCount, bool HasBaseGamePath, bool HasExpansionPath, int ArchiveCount)
{
    /// <summary>Whether anything shipped with the game could be found at all.</summary>
    public bool CanResolveShippedAssets => HasBaseGamePath || HasExpansionPath;

    /// <summary>A sentence naming what is missing, for the preview to show verbatim.</summary>
    public string Explain()
    {
        if (!CanResolveShippedAssets)
            return "Only this workspace's own files can be resolved. Set " +
                   "aet-eaw-edit.lsp.source.baseGameDirectory (and expansionDirectory) to preview " +
                   "models that ship with the game.";

        var where = (HasBaseGamePath, HasExpansionPath) switch
        {
            (true, true) => "the base game and the expansion",
            (true, false) => "the base game",
            _ => "the expansion"
        };

        return $"Resolving from this workspace, {where}, and {ArchiveCount} archive(s).";
    }
}

/// <summary>
///     Reads MEG archives from the configured game directories.
/// </summary>
/// <remarks>
///     A seam rather than a direct dependency so the resolver's precedence rules can be tested without
///     building real archives, and so a workspace with no game directory configured pays nothing.
/// </remarks>
public interface IMegArchiveSet
{
    int ArchiveCount { get; }

    /// <summary>The bytes of <paramref name="normalizedPath" />, or null when no archive holds it.</summary>
    byte[]? TryRead(string normalizedPath);
}

/// <summary>The archive tier for a workspace with no game directory configured.</summary>
public sealed class EmptyArchives : IMegArchiveSet
{
    public static readonly EmptyArchives Instance = new();

    public int ArchiveCount => 0;

    public byte[]? TryRead(string normalizedPath)
    {
        return null;
    }
}

/// <summary>Resolves a game-relative asset path to actual bytes.</summary>
public interface IGameAssetResolver
{
    /// <summary>Which tiers are available, and why one is not.</summary>
    GameAssetTiers Tiers { get; }

    /// <summary>Where <paramref name="gameRelativePath" /> resolves, or null when nowhere.</summary>
    GameAssetLocation? Locate(string gameRelativePath);

    /// <summary>The bytes of <paramref name="gameRelativePath" />, or null when it resolves nowhere.</summary>
    byte[]? Read(string gameRelativePath);
}

/// <summary>
///     Resolves a game-relative path the way the engine's virtual filesystem would.
/// </summary>
/// <remarks>
///     <para>
///         Precedence, highest first: the workspace's own asset roots (its layers in rank order), the
///         configured expansion directory, the configured base game directory, then their archives.
///         That mirrors the engine - a mod shadows the expansion, which shadows the base game, and a
///         loose file shadows the archives - and getting it wrong would make a modder's own
///         replacement asset silently invisible in the preview.
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
/// </remarks>
public sealed class GameAssetResolver(
    IFileHelper fileHelper,
    ILspConfigurationProvider config,
    IModProjectReloadService? projects,
    IMegArchiveSet archives,
    ILogger<GameAssetResolver> logger) : IGameAssetResolver
{
    /// <summary>
    ///     Extensions the engine treats as one asset. A model references a texture by a <c>.tga</c>
    ///     name that frequently only ever ships as <c>.dds</c>, so honouring the written extension
    ///     alone would leave most shipped models untextured.
    /// </summary>
    private static readonly string[] InterchangeableTextureExtensions = [".tga", ".dds"];

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

        // Interchange is applied WITHIN each tier before moving to the next, so a mod shipping the
        // .dds beats the base game's .tga. The other order would make a mod's own texture unreachable.
        var candidates = Candidates(normalized);

        // Root outer, candidate inner - a whole layer is exhausted, alternate extensions included,
        // before the next is consulted. The other nesting would let a dependency's .tga beat the root
        // project's .dds, which inverts the layering the rest of the server agrees on.
        foreach (var root in WorkspaceRoots())
        foreach (var candidate in candidates)
        foreach (var relative in WorkspaceCandidates(candidate))
        {
            var hit = fileHelper.FindInWorkspace([root], relative);
            if (hit is not null)
                return new GameAssetLocation(gameRelativePath, hit, GameAssetTier.Workspace);
        }

        foreach (var (root, tier) in GameDirectories())
        foreach (var candidate in candidates)
        {
            // One root at a time so the tier reported is the directory that actually supplied it.
            var hit = fileHelper.FindInWorkspace([root], candidate);
            if (hit is not null)
                return new GameAssetLocation(gameRelativePath, hit, tier);
        }

        foreach (var candidate in candidates)
            if (archives.TryRead(candidate) is not null)
                return new GameAssetLocation(gameRelativePath, candidate, GameAssetTier.Archive);

        return null;
    }

    public byte[]? Read(string gameRelativePath)
    {
        var location = Locate(gameRelativePath);
        if (location is null)
            return null;

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
                gameRelativePath, location.ResolvedPath);
            return null;
        }
    }

    /// <summary>
    ///     The requested path first, then the same name with each interchangeable extension.
    /// </summary>
    private static List<string> Candidates(string normalized)
    {
        var candidates = new List<string> { normalized };

        var ext = Path.GetExtension(normalized);
        if (!InterchangeableTextureExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return candidates;

        var stem = normalized[..^ext.Length];
        foreach (var other in InterchangeableTextureExtensions)
            if (!other.Equals(ext, StringComparison.OrdinalIgnoreCase))
                candidates.Add(stem + other);

        return candidates;
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
    ///     The directories a <c>.pgproj</c> can name as asset roots, as game-relative prefixes.
    /// </summary>
    private static readonly string[] AssetRootPrefixes = ["data/art/", "data/audio/"];

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
    ///     The configured game directories, expansion first - the order the engine layers them in.
    /// </summary>
    private List<(string Root, GameAssetTier Tier)> GameDirectories()
    {
        var directories = new List<(string, GameAssetTier)>(2);

        var expansion = config.Current.ExpansionPath;
        if (!string.IsNullOrWhiteSpace(expansion))
            directories.Add((expansion, GameAssetTier.ExpansionLoose));

        var baseGame = config.Current.GamePath;
        if (!string.IsNullOrWhiteSpace(baseGame))
            directories.Add((baseGame, GameAssetTier.BaseGameLoose));

        return directories;
    }
}
