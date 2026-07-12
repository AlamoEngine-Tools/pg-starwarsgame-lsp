// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Story.Discovery;

/// <summary>One faction's story attachment in a campaign: which plot manifest it loads.</summary>
public sealed record StoryFactionManifest(string Faction, string ManifestFile);

/// <summary>A campaign and its per-faction plot manifests, in document order.</summary>
public sealed record StoryCampaignChain(string Name, IReadOnlyList<StoryFactionManifest> FactionManifests)
{
    /// <summary>
    ///     Xml-relative path of the campaign set file that declared this campaign — the target
    ///     for manifest attach/detach mutations. Empty for pre-mutation-era cached results.
    /// </summary>
    public string SourceFile { get; init; } = "";
}

/// <summary>The parsed entries of one plot manifest (xml-relative thread files, raw Lua names).</summary>
public sealed record StoryManifestContents(
    string ManifestFile,
    IReadOnlyList<string> ActiveThreads,
    IReadOnlyList<string> SuspendedThreads,
    IReadOnlyList<string> LuaScripts);

/// <summary>A tactical plot manifest referenced from a thread event.</summary>
public sealed record StoryTacticalReference(string ThreadFile, string ManifestFile);

/// <summary>
///     Everything the campaign story chain reaches: plot manifest files (to be typed
///     <c>StoryPlotManifest</c>), story thread files (to be typed <c>StoryParser</c>), attached
///     Lua script names (extensionless, as written), and the problems found on the way. File
///     lists are xml-directory-relative paths, de-duplicated case-insensitively. The structured
///     views (<see cref="Campaigns" />, <see cref="Manifests" />,
///     <see cref="TacticalReferences" />) preserve the associations the walk followed, so
///     per-campaign story models can be assembled without re-walking.
/// </summary>
public sealed record StoryChainScanResult(
    IReadOnlyList<string> ManifestFiles,
    IReadOnlyList<string> ThreadFiles,
    IReadOnlyList<string> LuaScripts,
    IReadOnlyList<StoryChainProblem> Problems)
{
    public static readonly StoryChainScanResult Empty = new([], [], [], []);

    public IReadOnlyList<StoryCampaignChain> Campaigns { get; init; } = [];
    public IReadOnlyList<StoryManifestContents> Manifests { get; init; } = [];
    public IReadOnlyList<StoryTacticalReference> TacticalReferences { get; init; } = [];
}
