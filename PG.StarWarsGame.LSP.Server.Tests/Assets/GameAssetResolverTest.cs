// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Assets;

/// <summary>
///     Resolving a game-relative asset path through the workspace, then the configured game
///     directories, then their archives.
/// </summary>
/// <remarks>
///     <para>
///         The precedence mirrors how the engine layers its virtual filesystem: a mod shadows the
///         expansion, which shadows the base game, and within any one of those a loose file shadows the
///         archives. Get the order wrong and a modder's own replacement asset silently does not appear
///         in the preview, which is the single most confusing failure this feature could have.
///     </para>
///     <para>
///         There is deliberately <strong>no install detection</strong>. The game directories come only
///         from configuration, because probing for an install means reading the Windows registry and
///         that would foreclose the Linux support we want to keep open.
///     </para>
/// </remarks>
public sealed class GameAssetResolverTest
{
    private const string BaseGame = @"C:\games\eaw";
    private const string Expansion = @"C:\games\foc";

    private const string ModelPath = "Data/Art/Models/Ev_stardestroyer.alo";

    private static GameAssetResolver Build(
        MockFileSystem fs,
        IReadOnlyList<string>? assetRoots = null,
        string? gamePath = null,
        string? expansionPath = null,
        IMegArchiveSet? archives = null)
    {
        var config = new StubConfiguration(new LspConfiguration
        {
            GamePath = gamePath,
            ExpansionPath = expansionPath
        });

        var projects = new StubProjects(assetRoots is null
            ? null
            : WorkspaceConfiguration.Empty with
            {
                AssetRoots = assetRoots,
                Layers = [.. assetRoots.Select((r, i) => new ProjectLayer(
                    assetRoots.Count - i, $"layer{i}", [], [], [], [r], null))]
            });

        return new GameAssetResolver(new FileHelper(fs), config, projects,
            archives ?? EmptyArchives.Instance, NullLogger<GameAssetResolver>.Instance);
    }

    private static MockFileSystem FileSystemWith(params string[] paths)
    {
        var fs = new MockFileSystem();
        foreach (var path in paths)
            fs.AddFile(path, new MockFileData([1, 2, 3]));
        return fs;
    }

    // ── tiers and precedence ──────────────────────────────────────────────────

    [Fact]
    public void Locate_FindsAnAssetInAWorkspaceRoot()
    {
        var fs = FileSystemWith(@"C:\mod\Data\Art\Models\Ev_stardestroyer.alo");
        var resolver = Build(fs, assetRoots: [@"C:\mod"]);

        var found = resolver.Locate(ModelPath);

        Assert.NotNull(found);
        Assert.Equal(GameAssetTier.Workspace, found.Tier);
    }

    [Fact]
    public void Locate_FindsAnAssetUnderAnArtRoot()
    {
        // What a real workspace actually looks like. A .pgproj declares `directories.art`, and
        // ModProjectResolver turns that into the asset roots, so a root is `<project>/data/art` -
        // NOT the game root. Searching it for the whole `Data/Art/Models/...` looks under
        // `<project>/data/art/Data/Art/Models/...`, which never exists, and every model in the
        // modder's own workspace reports "not found".
        var fs = FileSystemWith(@"C:\mod\data\art\Models\Ev_stardestroyer.alo");
        var resolver = Build(fs, assetRoots: [@"C:\mod\data\art"]);

        var found = resolver.Locate(ModelPath);

        Assert.NotNull(found);
        Assert.Equal(GameAssetTier.Workspace, found.Tier);
    }

    [Fact]
    public void Locate_FindsATextureUnderAnArtRoot()
    {
        var fs = FileSystemWith(@"C:\mod\data\art\Textures\Ai_rancor.tga");
        var resolver = Build(fs, assetRoots: [@"C:\mod\data\art"]);

        Assert.NotNull(resolver.Locate("Data/Art/Textures/Ai_rancor.tga"));
    }

    [Fact]
    public void Locate_StillFindsAnAssetUnderARootThatIsTheGameDirectory()
    {
        // The shortened path is tried IN ADDITION to the full one, never instead of it, so a root
        // that does point at a game directory keeps working.
        var fs = FileSystemWith(@"C:\mod\Data\Art\Models\Ev_stardestroyer.alo");

        Assert.NotNull(Build(fs, assetRoots: [@"C:\mod"]).Locate(ModelPath));
    }

    [Fact]
    public void Locate_DoesNotInventAHitFromTheShortenedPath()
    {
        // Dropping the prefix must not turn an unrelated file into a match.
        var fs = FileSystemWith(@"C:\mod\data\art\Something\Else.alo");

        Assert.Null(Build(fs, assetRoots: [@"C:\mod\data\art"]).Locate(ModelPath));
    }

    [Fact]
    public void Locate_PrefersTheWorkspaceOverBothGameDirectories()
    {
        var fs = FileSystemWith(
            @"C:\mod\Data\Art\Models\Ev_stardestroyer.alo",
            @"C:\games\foc\Data\Art\Models\Ev_stardestroyer.alo",
            @"C:\games\eaw\Data\Art\Models\Ev_stardestroyer.alo");
        var resolver = Build(fs, [@"C:\mod"], BaseGame, Expansion);

        Assert.Equal(GameAssetTier.Workspace, resolver.Locate(ModelPath)!.Tier);
    }

    [Fact]
    public void Locate_PrefersTheExpansionOverTheBaseGame()
    {
        // FoC layers on top of EaW, exactly as the baseline builder loads them.
        var fs = FileSystemWith(
            @"C:\games\foc\Data\Art\Models\Ev_stardestroyer.alo",
            @"C:\games\eaw\Data\Art\Models\Ev_stardestroyer.alo");
        var resolver = Build(fs, null, BaseGame, Expansion);

        var found = resolver.Locate(ModelPath);

        Assert.Equal(GameAssetTier.ExpansionLoose, found!.Tier);
        Assert.Contains("foc", found.ResolvedPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Locate_FallsBackToTheBaseGameWhenTheExpansionLacksTheAsset()
    {
        var fs = FileSystemWith(@"C:\games\eaw\Data\Art\Models\Ev_stardestroyer.alo");
        var resolver = Build(fs, null, BaseGame, Expansion);

        Assert.Equal(GameAssetTier.BaseGameLoose, resolver.Locate(ModelPath)!.Tier);
    }

    [Fact]
    public void Locate_UsesTheHighestRankedWorkspaceLayerFirst()
    {
        // Asset roots arrive highest-precedence first; a dependency must not shadow the root project.
        var fs = FileSystemWith(
            @"C:\mod\Data\Art\Models\Ev_stardestroyer.alo",
            @"C:\dependency\Data\Art\Models\Ev_stardestroyer.alo");
        var resolver = Build(fs, [@"C:\mod", @"C:\dependency"]);

        Assert.Contains(@"C:\mod", resolver.Locate(ModelPath)!.ResolvedPath,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── no install detection ──────────────────────────────────────────────────

    [Fact]
    public void Locate_WithNoConfiguredGamePaths_ConsultsOnlyTheWorkspace()
    {
        // A game tree exists on disk but was never configured, so it must be invisible. Probing for it
        // would mean registry reads, and Linux has no registry.
        var fs = FileSystemWith(@"C:\games\eaw\Data\Art\Models\Ev_stardestroyer.alo");
        var resolver = Build(fs, assetRoots: [@"C:\mod"]);

        Assert.Null(resolver.Locate(ModelPath));
    }

    [Fact]
    public void Tiers_ReportWhyShippedAssetsCannotResolve()
    {
        var resolver = Build(new MockFileSystem(), assetRoots: [@"C:\mod"]);

        var tiers = resolver.Tiers;

        Assert.False(tiers.CanResolveShippedAssets);
        Assert.Equal(1, tiers.WorkspaceRootCount);
        Assert.False(tiers.HasBaseGamePath);
        Assert.False(tiers.HasExpansionPath);
        Assert.Contains("baseGameDirectory", tiers.Explain(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tiers_ReportShippedAssetsAsResolvableOnceAGamePathIsConfigured()
    {
        var resolver = Build(new MockFileSystem(), null, BaseGame);

        Assert.True(resolver.Tiers.CanResolveShippedAssets);
    }

    // ── extension interchange ─────────────────────────────────────────────────

    [Fact]
    public void Locate_ResolvesATgaRequestAgainstADdsOnDisk()
    {
        // The engine treats the two as one asset; the shipped models reference .tga names for textures
        // that only ever ship as .dds.
        var fs = FileSystemWith(@"C:\games\eaw\Data\Art\Textures\Ai_rancor.dds");
        var resolver = Build(fs, null, BaseGame);

        var found = resolver.Locate("Data/Art/Textures/Ai_rancor.tga");

        Assert.NotNull(found);
        Assert.EndsWith(".dds", found.ResolvedPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Locate_PrefersTheRequestedExtensionWithinATier()
    {
        var fs = FileSystemWith(
            @"C:\games\eaw\Data\Art\Textures\Ai_rancor.dds",
            @"C:\games\eaw\Data\Art\Textures\Ai_rancor.tga");
        var resolver = Build(fs, null, BaseGame);

        Assert.EndsWith(".tga", resolver.Locate("Data/Art/Textures/Ai_rancor.tga")!.ResolvedPath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Locate_LetsAModsAlternateExtensionShadowTheBaseGamesExactMatch()
    {
        // Interchange is applied WITHIN a tier before moving to the next, so a mod shipping the .dds
        // wins over the base game's .tga. The other order would make a mod's own texture unreachable.
        var fs = FileSystemWith(
            @"C:\mod\Data\Art\Textures\Ai_rancor.dds",
            @"C:\games\eaw\Data\Art\Textures\Ai_rancor.tga");
        var resolver = Build(fs, [@"C:\mod"], BaseGame);

        Assert.Equal(GameAssetTier.Workspace, resolver.Locate("Data/Art/Textures/Ai_rancor.tga")!.Tier);
    }

    [Fact]
    public void Locate_DoesNotLetADependencysExactMatchBeatTheRootProjectsAlternate()
    {
        // Layer precedence dominates extension preference: the root project owns the asset even when
        // it ships the other format. Nesting these loops the other way round inverts the layering.
        var fs = FileSystemWith(
            @"C:\mod\Data\Art\Textures\Ai_rancor.dds",
            @"C:\dependency\Data\Art\Textures\Ai_rancor.tga");
        var resolver = Build(fs, [@"C:\mod", @"C:\dependency"]);

        Assert.Contains(@"C:\mod", resolver.Locate("Data/Art/Textures/Ai_rancor.tga")!.ResolvedPath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Locate_DoesNotInterchangeExtensionsThatAreNotTextures()
    {
        var fs = FileSystemWith(@"C:\games\eaw\Data\Art\Models\Ev_stardestroyer.ala");
        var resolver = Build(fs, null, BaseGame);

        Assert.Null(resolver.Locate(ModelPath));
    }

    // ── path handling ─────────────────────────────────────────────────────────

    [Fact]
    public void Locate_MatchesPathSegmentsCaseInsensitivelyOnAnyPlatform()
    {
        // The shipped data is mixed case and the XML that references it is not consistent with it.
        // Matching is done explicitly rather than relying on a case-insensitive filesystem, so Linux
        // behaves the same as Windows.
        var fs = FileSystemWith(@"C:\games\eaw\DATA\ART\MODELS\EV_STARDESTROYER.ALO");
        var resolver = Build(fs, null, BaseGame);

        Assert.NotNull(resolver.Locate("data/art/models/ev_stardestroyer.alo"));
    }

    [Fact]
    public void Locate_AcceptsBackslashSeparatedPathsAsTheGameWritesThem()
    {
        var fs = FileSystemWith(@"C:\games\eaw\Data\Art\Models\Ev_stardestroyer.alo");
        var resolver = Build(fs, null, BaseGame);

        Assert.NotNull(resolver.Locate(@"Data\Art\Models\Ev_stardestroyer.alo"));
    }

    [Fact]
    public void Read_ReturnsTheBytesOfTheResolvedFile()
    {
        var fs = new MockFileSystem();
        fs.AddFile(@"C:\games\eaw\Data\Art\Models\Ev_stardestroyer.alo",
            new MockFileData([0xDE, 0xAD, 0xBE, 0xEF]));
        var resolver = Build(fs, null, BaseGame);

        Assert.Equal<byte[]>([0xDE, 0xAD, 0xBE, 0xEF], resolver.Read(ModelPath)!);
    }

    [Fact]
    public void Read_ReturnsNullForAnAssetThatResolvesNowhere()
    {
        Assert.Null(Build(new MockFileSystem(), null, BaseGame).Read(ModelPath));
    }

    // ── archives ──────────────────────────────────────────────────────────────

    [Fact]
    public void Locate_FallsBackToAnArchiveWhenNoLooseFileMatches()
    {
        var archives = new StubArchives(("data/art/models/ev_stardestroyer.alo", [7, 7, 7]));
        var resolver = Build(new MockFileSystem(), null, BaseGame, archives: archives);

        var found = resolver.Locate(ModelPath);

        Assert.Equal(GameAssetTier.Archive, found!.Tier);
        Assert.Equal<byte[]>([7, 7, 7], resolver.Read(ModelPath)!);
    }

    [Fact]
    public void Locate_PrefersALooseFileOverTheSameAssetInAnArchive()
    {
        // Loose files shadow the archives in the engine's virtual filesystem, which is exactly how a
        // modder overrides shipped content without repacking.
        var fs = FileSystemWith(@"C:\games\eaw\Data\Art\Models\Ev_stardestroyer.alo");
        var archives = new StubArchives(("data/art/models/ev_stardestroyer.alo", [7, 7, 7]));
        var resolver = Build(fs, null, BaseGame, archives: archives);

        Assert.Equal(GameAssetTier.BaseGameLoose, resolver.Locate(ModelPath)!.Tier);
    }
}
