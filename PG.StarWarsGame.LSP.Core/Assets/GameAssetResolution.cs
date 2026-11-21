// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;
using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Core.Assets;

/// <summary>Which layer of the virtual filesystem supplied an asset.</summary>
public enum GameAssetTier
{
    /// <summary>A loose file under one of the workspace's own asset roots.</summary>
    Workspace,

    /// <summary>A loose file under the configured expansion (Forces of Corruption) directory.</summary>
    ExpansionLoose,

    /// <summary>A loose file under the configured base game (Empire at War) directory.</summary>
    BaseGameLoose,

    /// <summary>An entry inside one of the MEG archives the game loads.</summary>
    Archive
}

/// <summary>Where an asset was found.</summary>
/// <param name="ResolvedPath">
///     The absolute file path, or the archive-relative entry path for <see cref="GameAssetTier.Archive" />.
/// </param>
[PublicAPI]
public sealed record GameAssetLocation(
    string RequestedPath,
    string ResolvedPath,
    GameAssetTier Tier);

/// <summary>
///     Which tiers this workspace can currently resolve from.
/// </summary>
/// <remarks>
///     Reported rather than inferred so the preview can explain a miss. "This model is not in your
///     mod and you have not told the extension where the game is" is actionable; "model not found"
///     is not.
/// </remarks>
public sealed record GameAssetTiers(
    int WorkspaceRootCount,
    bool HasBaseGamePath,
    bool HasExpansionPath,
    int ArchiveCount)
{
    /// <summary>Whether anything shipped with the game could be found at all.</summary>
    public bool CanResolveShippedAssets => HasBaseGamePath || HasExpansionPath;

    /// <summary>A sentence naming what is missing, for the preview to show verbatim.</summary>
    public string Explain(IMessageCatalog messages)
    {
        if (!CanResolveShippedAssets)
            return messages.Format("AssetTiers_WorkspaceOnly");

        var where = (HasBaseGamePath, HasExpansionPath) switch
        {
            (true, true) => messages.Format("AssetTiers_BaseGameAndExpansion"),
            (true, false) => messages.Format("AssetTiers_BaseGame"),
            _ => messages.Format("AssetTiers_Expansion")
        };

        return messages.Format("AssetTiers_Resolving", where, ArchiveCount);
    }
}

/// <summary>
///     Reads the MEG archives the game loads, from the projects and the configured game directories.
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

    /// <summary>
    ///     Has the archives read again before the next answer: one of them changed on disk, was
    ///     added or was removed.
    /// </summary>
    void Invalidate()
    {
    }
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

    /// <summary>Where <paramref name="gameRelativePath" /> resolves, exactly as written, or null when nowhere.</summary>
    GameAssetLocation? Locate(string gameRelativePath);

    /// <summary>
    ///     Where the texture <paramref name="gameRelativePath" /> names resolves by the game's texture lookup,
    ///     or null when nowhere: each of <see cref="GameTextureName.Candidates" /> in turn, through every tier.
    /// </summary>
    /// <remarks>
    ///     Candidate outer, tier inner, as measured in the game (FoC x64, 2024 release, #420): the texture
    ///     loader opens the name as written through the game's ordinary file open - mod folders, then loose
    ///     files, then archives - and only when that fails everywhere does it open the <c>.dds</c>, the same
    ///     way. So a base game's <c>.tga</c> is what a <c>.tga</c> name opens, not a mod's <c>.dds</c>. The
    ///     per-tier order of one open is <see cref="Locate" />'s.
    /// </remarks>
    GameAssetLocation? LocateTexture(string gameRelativePath)
    {
        foreach (var candidate in GameTextureName.Candidates(gameRelativePath))
            if (Locate(candidate) is { } found)
                return found;

        return null;
    }

    /// <summary>The bytes of <paramref name="gameRelativePath" />, or null when it resolves nowhere.</summary>
    byte[]? Read(string gameRelativePath);

    /// <summary>
    ///     The bytes at a location <see cref="Locate" /> returned, without resolving the path a second
    ///     time. Null when it can no longer be read.
    /// </summary>
    byte[]? Read(GameAssetLocation location)
    {
        return Read(location.RequestedPath);
    }
}
