// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PG.Commons;
using PG.StarWarsGame.Files.MEG;
using PG.StarWarsGame.Files.MEG.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Tests.Assets;

/// <summary>
///     The archive tier, against the real MEG files in the repository's extracted game tree.
/// </summary>
/// <remarks>
///     <see cref="GameAssetResolverTest" /> covers precedence with a stubbed archive set, which proves
///     the ordering rules but says nothing about whether we can open an actual archive. This opens
///     real ones - the SFX archives are the only MEGs the extracted trees carry - so a break in the
///     MEG service wiring or the load-order resolution surfaces here rather than the first time
///     someone with a real install opens a preview.
/// </remarks>
public sealed class MegArchiveSetTest
{
    private static MegArchiveSet Build(IFileSystem fileSystem, string? gamePath)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fileSystem);
        PetroglyphCommons.ContributeServices(services);
        services.SupportMEG();
        var provider = services.BuildServiceProvider();

        return new MegArchiveSet(
            fileSystem,
            new StubConfiguration(new LspConfiguration { GamePath = gamePath }),
            provider.GetRequiredService<IMegFileService>(),
            provider.GetRequiredService<IMegFileExtractor>(),
            NullLogger<MegArchiveSet>.Instance);
    }

    [Fact]
    public void ArchiveCount_WithNoGamePathConfigured_IsZeroAndReadsNothing()
    {
        var archives = Build(new MockFileSystem(), null);

        Assert.Equal(0, archives.ArchiveCount);
        Assert.Null(archives.TryRead("data/art/models/anything.alo"));
    }

    [Fact]
    public void ArchiveCount_WithAPathThatDoesNotExist_IsZero()
    {
        // A stale setting must degrade to "no archives", not throw on a missing directory.
        var archives = Build(new MockFileSystem(), @"C:\nope\not\here");

        Assert.Equal(0, archives.ArchiveCount);
    }

    [Fact]
    public void ArchiveCount_OpensTheRealArchivesInTheExtractedTree()
    {
        var tree = FindGameTree();
        if (tree is null)
            Assert.Skip("No extracted game tree found next to the repository root.");

        var archives = Build(new FileSystem(), tree);

        // The extracted trees carry the three SFX archives and no others. Asserting "some" rather
        // than an exact count keeps this from breaking if a tree gains one.
        Assert.True(archives.ArchiveCount >= 3,
            $"Expected the SFX archives to be indexed, found {archives.ArchiveCount}.");
    }

    [Fact]
    public void TryRead_ForAPathNoArchiveHolds_ReturnsNull()
    {
        var tree = FindGameTree();
        if (tree is null)
            Assert.Skip("No extracted game tree found next to the repository root.");

        var archives = Build(new FileSystem(), tree);

        Assert.Null(archives.TryRead("data/art/models/definitely_not_in_a_meg.alo"));
    }

    private static string? FindGameTree()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "eaw");
            if (Directory.Exists(Path.Combine(candidate, "Data")))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private sealed class StubConfiguration(LspConfiguration current) : ILspConfigurationProvider
    {
        public LspConfiguration Current { get; } = current;

        public void LoadFrom(object? initializationOptions)
        {
        }
    }
}
