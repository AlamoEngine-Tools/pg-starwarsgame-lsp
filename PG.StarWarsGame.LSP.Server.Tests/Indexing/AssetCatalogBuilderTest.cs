// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Indexing;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Server.Tests.Indexing;

/// <summary>
///     The asset catalog on its own (#315): loose files under the asset and script roots, the
///     archives the game loads from the project folders, merged game-relative over the baseline's.
/// </summary>
public sealed class AssetCatalogBuilderTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    /// <summary>One project at <paramref name="projectRoot" /> whose asset roots are <paramref name="assetRoots" />.</summary>
    private static WorkspaceConfiguration AssetProject(string projectRoot, params string[] assetRoots)
    {
        return new WorkspaceConfiguration([], [], [], assetRoots, null)
        {
            Layers = [AssetLayer(0, projectRoot, assetRoots)]
        };
    }

    private static ProjectLayer AssetLayer(int rank, string projectRoot, params string[] assetRoots)
    {
        return new ProjectLayer(rank, $"layer{rank}", [], [], [], assetRoots, null,
            Path.Combine(projectRoot, "mod.pgproj"));
    }

    private static string MegaFiles(params string[] entries)
    {
        return "<?xml version=\"1.0\" ?>\n<Mega_Files>\n" +
               string.Concat(entries.Select(e => $"\t<File> {e} </File>\n")) + "</Mega_Files>";
    }

    private static AssetCatalogBuilder Build(MockFileSystem fs, IGameIndexService svc,
        IMegEntryReader? megReader = null)
    {
        var fh = new FileHelper(fs);
        return new AssetCatalogBuilder(fh, svc, new FakeLspConfigurationProvider(),
            megReader ?? NoMegEntries.Instance, new MegArchiveLoadOrder(fh),
            NullLogger<AssetCatalogBuilder>.Instance);
    }

    private sealed class FakeMegEntryReader(params (string Path, IReadOnlyList<string> Entries)[] archives)
        : IMegEntryReader
    {
        public IEnumerable<string> EnumerateEntryPaths(string path)
        {
            var normalized = path.Replace('/', '\\');
            return archives.FirstOrDefault(a => a.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                .Entries ?? [];
        }
    }

    [Fact]
    public void Apply_MergesBaselineAndWorkspace()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "Art", "Textures", "Local.tga")] = new("")
        });
        var baseline = BaselineIndex.Empty with
        {
            AssetFiles = ImmutableHashSet.Create("data/art/textures/shipped.tga")
        };
        var svc = new FakeGameIndexService(GameIndex.Empty with { Baseline = baseline });
        var builder = Build(fs, svc);

        builder.Apply(AssetProject(root, root));

        Assert.True(svc.Current.AssetFiles.Contains("data/art/textures/local.tga"));
        Assert.True(svc.Current.AssetFiles.Contains("data/art/textures/shipped.tga"));
    }

    /// <summary>
    ///     A workspace asset is catalogued by its GAME-relative path, not by its path relative to
    ///     whichever asset root happened to find it.
    /// </summary>
    /// <remarks>
    ///     A .pgproj declares its asset roots as <c>data/audio</c>, <c>data/art</c> and so on, so
    ///     normalising against the root that matched threw the <c>data/audio/</c> prefix away and
    ///     stored <c>announcer/foo.wav</c>. The baseline stores <c>data/...</c>, and references are
    ///     written game-relative too - EaWX writes <c>Data\Audio\Announcer\foo.wav</c> - so the two
    ///     halves of the merged catalog never met and every such reference read as a missing file.
    /// </remarks>
    [Fact]
    public void Apply_StoresWorkspaceAssetsGameRelative()
    {
        var root = Root("ws");
        var audioRoot = Path.Combine(root, "Data", "Audio");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(audioRoot, "Announcer", "Destroyed_ENG.wav")] = new("")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var builder = Build(fs, svc);

        // The asset ROOT is the audio directory itself, which is how a .pgproj declares it.
        builder.Apply(AssetProject(root, audioRoot));

        Assert.True(svc.Current.AssetFiles.Contains("data/audio/announcer/destroyed_eng.wav"),
            "expected the game-relative path, got: " + string.Join(", ", svc.Current.AssetFiles));
    }

    /// <summary>
    ///     A loose script is catalogued from the project's SCRIPT roots, which a .pgproj declares
    ///     apart from its asset roots, game-relative like every other entry.
    /// </summary>
    [Fact]
    public void Apply_CataloguesLooseScriptsFromTheScriptRoots()
    {
        var root = Root("ws");
        var scripts = Path.Combine(root, "Data", "Scripts");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(scripts, "GameObject", "BobaFettPlan.lua")] = new("")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var builder = Build(fs, svc);

        builder.Apply(new WorkspaceConfiguration([], [scripts], [], [], null));

        Assert.True(svc.Current.AssetFiles.Contains("data/scripts/gameobject/bobafettplan.lua"),
            "expected the script, got: " + string.Join(", ", svc.Current.AssetFiles));
    }

    /// <summary>
    ///     A root with no <c>data</c> segment above the file still yields something usable: the
    ///     path relative to the root, as before. Nothing outside a game tree is guessed at.
    /// </summary>
    [Fact]
    public void Apply_RootOutsideAGameTree_KeepsTheRootRelativePath()
    {
        var root = Root("loose");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "extra", "stray.tga")] = new("")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var builder = Build(fs, svc);

        builder.Apply(AssetProject(root, root));

        Assert.True(svc.Current.AssetFiles.Contains("extra/stray.tga"));
    }

    /// <summary>
    ///     What a project's archive holds counts as present, with no loose copy anywhere (#220).
    /// </summary>
    /// <remarks>
    ///     A mod ships its assets packed in <c>Data\</c>, beside its art and audio folders rather
    ///     than under them, and names the archives in its own <c>Data\MegaFiles.xml</c>. Only the
    ///     art and audio roots were searched for archives, so every reference into a mod's own
    ///     archives read as a missing file.
    /// </remarks>
    [Fact]
    public void Apply_ReadsTheArchivesTheProjectsListNames()
    {
        var root = Root("ws");
        var art = Path.Combine(root, "Data", "Art");
        var meg = Path.Combine(root, "Data", "ModTextures.meg");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "MegaFiles.xml")] = new(MegaFiles(@"Data\ModTextures.meg")),
            [meg] = new("not a real archive - the reader is the seam")
        });
        fs.AddDirectory(art);
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var builder = Build(fs, svc,
            new FakeMegEntryReader((meg, [@"DATA\ART\TEXTURES\PACKED.TGA"])));

        builder.Apply(AssetProject(root, art));

        Assert.True(svc.Current.AssetFiles.Contains("data/art/textures/packed.tga"));
    }

    /// <summary>The game loads no archive its list does not name, so neither does the catalog.</summary>
    [Fact]
    public void Apply_AnArchiveNoListNames_IsNotRead()
    {
        var root = Root("ws");
        var meg = Path.Combine(root, "Data", "Art", "Stray.meg");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "MegaFiles.xml")] = new(MegaFiles(@"Data\ModTextures.meg")),
            [meg] = new("not a real archive - the reader is the seam")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var builder = Build(fs, svc,
            new FakeMegEntryReader((meg, [@"DATA\ART\TEXTURES\STRAY.TGA"])));

        builder.Apply(AssetProject(root, Path.Combine(root, "Data", "Art")));

        Assert.False(svc.Current.AssetFiles.Contains("data/art/textures/stray.tga"));
    }

    /// <summary>
    ///     A dependency's archives count too, its list read when the leaf has none, and a leaf
    ///     archive of a listed name replaces the dependency's.
    /// </summary>
    [Fact]
    public void Apply_ReadsTheDependencysArchives_InLayerOrder()
    {
        var leaf = Root("leaf");
        var dependency = Root("dependency");
        var shared = Path.Combine(dependency, "Data", "Shared.meg");
        var dependencyModels = Path.Combine(dependency, "Data", "Models.meg");
        var leafModels = Path.Combine(leaf, "Data", "Models.meg");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(dependency, "Data", "MegaFiles.xml")] =
                new(MegaFiles(@"Data\Shared.meg", @"Data\Models.meg")),
            [shared] = new(""),
            [dependencyModels] = new(""),
            [leafModels] = new("")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var builder = Build(fs, svc, new FakeMegEntryReader(
            (shared, [@"DATA\ART\TEXTURES\SHARED.TGA"]),
            (dependencyModels, [@"DATA\ART\MODELS\REPLACED.ALO"]),
            (leafModels, [@"DATA\ART\MODELS\LEAF.ALO"])));
        var config = new WorkspaceConfiguration([], [], [], [], null)
        {
            Layers = [AssetLayer(0, dependency), AssetLayer(1, leaf)]
        };

        builder.Apply(config);

        Assert.True(svc.Current.AssetFiles.Contains("data/art/textures/shared.tga"));
        Assert.True(svc.Current.AssetFiles.Contains("data/art/models/leaf.alo"));
        Assert.False(svc.Current.AssetFiles.Contains("data/art/models/replaced.alo"));
    }

    /// <summary>
    ///     An SFX archive IS the <c>data/audio/sfx</c> directory, so its flat entries are stored
    ///     under that prefix - the same rule the baseline builder applies, from the same code.
    /// </summary>
    [Fact]
    public void Apply_AppliesSfxConventionsToWorkspaceArchives()
    {
        var root = Root("ws");
        var meg = Path.Combine(root, "Data", "Audio", "SFX", "sfx2d_english.meg");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [meg] = new("not a real archive - the reader is the seam")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var builder = Build(fs, svc,
            new FakeMegEntryReader((meg, ["U000_SPD0101_ENG.WAV"])));

        builder.Apply(AssetProject(root, Path.Combine(root, "Data", "Audio")));

        Assert.True(svc.Current.AssetFiles.Contains("data/audio/sfx/u000_spd0101_eng.wav"));
    }
}