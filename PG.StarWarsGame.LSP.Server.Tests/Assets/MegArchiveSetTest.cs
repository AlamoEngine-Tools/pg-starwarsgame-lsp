// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PG.Commons;
using PG.StarWarsGame.Files.MEG;
using PG.StarWarsGame.Files.MEG.Data;
using PG.StarWarsGame.Files.MEG.Files;
using PG.StarWarsGame.Files.MEG.Services;
using PG.StarWarsGame.Files.MEG.Services.Builder;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Tests.Assets;

/// <summary>
///     The archive tier: the archives the game loads (#221), opened for real.
/// </summary>
/// <remarks>
///     <see cref="GameAssetResolverTest" /> covers precedence with a stubbed archive set, which proves
///     the ordering rules but says nothing about whether we can open an actual archive. These open
///     real ones - the extracted trees' SFX archives, and small archives built into a mock file
///     system - so a break in the MEG service wiring or the load order surfaces here rather than the
///     first time someone with a real install opens a preview.
/// </remarks>
public sealed class MegArchiveSetTest
{
    private const string Project = @"C:\mods\leaf";
    private const string Game = @"C:\game\eaw";

    private static IServiceProvider Services(IFileSystem fileSystem)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fileSystem);
        PetroglyphCommons.ContributeServices(services);
        services.SupportPetroglyphHashing();
        services.SupportMEG();
        return services.BuildServiceProvider();
    }

    private static MegArchiveSet Build(IFileSystem fileSystem, string? gamePath, string? projectRoot = null)
    {
        var provider = Services(fileSystem);
        var workspace = projectRoot is null
            ? null
            : WorkspaceConfiguration.Empty with
            {
                Layers = [new ProjectLayer(0, "leaf", [], [], [], [], null, Path.Combine(projectRoot, "mod.pgproj"))]
            };

        return new MegArchiveSet(
            new StubConfiguration(new LspConfiguration { GamePath = gamePath }),
            new StubProjects(workspace),
            new MegArchiveLoadOrder(new FileHelper(fileSystem)),
            provider.GetRequiredService<IMegService>(),
            NullLogger<MegArchiveSet>.Instance);
    }

    private static void WriteMeg(MockFileSystem fs, string path, params (string Entry, byte[] Bytes)[] entries)
    {
        fs.AddDirectory(Path.GetDirectoryName(path)!);
        var builder = new BinaryMegBuilder(Services(fs));
        foreach (var (entry, bytes) in entries)
            builder.AddBytes(bytes, entry);
        builder.Build(new MegFileInformation(path, MegVersion.V1), true);
    }

    private static string MegaFiles(params string[] entries)
    {
        return "<?xml version=\"1.0\" ?>\n<Mega_Files>\n" +
               string.Concat(entries.Select(e => $"\t<File> {e} </File>\n")) + "</Mega_Files>";
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

        // The extracted trees carry the three SFX archives and none of the archives their list
        // names. Asserting "some" rather than an exact count keeps this from breaking if a tree
        // gains one.
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

    /// <summary>
    ///     What the asset catalog finds in a project's archive, the preview can read (#221).
    /// </summary>
    /// <remarks>
    ///     Only the configured game folders were searched for archives, so a texture packed into the
    ///     mod's own archive was catalogued as present and then drawn as missing.
    /// </remarks>
    [Fact]
    public void TryRead_AnEntryOfAnArchiveTheProjectsListNames()
    {
        var fs = new MockFileSystem();
        fs.AddFile($@"{Project}\Data\MegaFiles.xml", new MockFileData(MegaFiles(@"Data\ModTextures.meg")));
        WriteMeg(fs, $@"{Project}\Data\ModTextures.meg", (@"DATA\ART\TEXTURES\PACKED.DDS", [4, 2]));

        var archives = Build(fs, null, Project);

        Assert.Equal<byte[]>([4, 2], archives.TryRead("data/art/textures/packed.dds")!);
    }

    /// <summary>
    ///     A sound archive's flat entries are read by the path the catalog gives them, under
    ///     <c>data/audio/sfx/</c>, or a catalogued sound would never resolve.
    /// </summary>
    [Fact]
    public void TryRead_ASoundArchiveEntry_ByItsCataloguedPath()
    {
        var fs = new MockFileSystem();
        WriteMeg(fs, $@"{Project}\Data\Audio\SFX\sfx2d_non_localized.meg", ("BLASTER.WAV", [9]));

        var archives = Build(fs, null, Project);

        Assert.Equal<byte[]>([9], archives.TryRead("data/audio/sfx/blaster.wav")!);
    }

    /// <summary>
    ///     The archive the game searches first answers: the LAST line of the list, not the
    ///     alphabetically last archive.
    /// </summary>
    [Fact]
    public void TryRead_TheArchiveSearchedFirstAnswers()
    {
        var fs = new MockFileSystem();
        fs.AddFile($@"{Game}\Data\MegaFiles.xml", new MockFileData(MegaFiles(@"Data\B.meg", @"Data\A.meg")));
        WriteMeg(fs, $@"{Game}\Data\B.meg", (@"DATA\ART\MODELS\SHIP.ALO", [2]));
        WriteMeg(fs, $@"{Game}\Data\A.meg", (@"DATA\ART\MODELS\SHIP.ALO", [1]));

        var archives = Build(fs, Game);

        Assert.Equal<byte[]>([1], archives.TryRead("data/art/models/ship.alo")!);
        Assert.Equal(2, archives.ArchiveCount);
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
}