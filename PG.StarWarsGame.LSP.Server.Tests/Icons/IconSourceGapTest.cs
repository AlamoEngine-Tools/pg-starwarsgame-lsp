// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Icons;

namespace PG.StarWarsGame.LSP.Server.Tests.Icons;

/// <summary>
///     Which projects in a workspace can supply no icons at all, so the preview can say why it is
///     drawing base-game artwork instead of the mod's.
/// </summary>
/// <remarks>
///     A project reaches the preview's icons one of two ways: the mega texture at the conventional
///     path, or loose source folders it declares in an <c>icons</c> node. A project with neither
///     contributes nothing, and before this it did so in SILENCE - every icon quietly fell through
///     to the baked base game and the card looked like a rendering bug rather than a missing
///     setting.
/// </remarks>
public sealed class IconSourceGapTest
{
    private const string LeafDir = "C:/mods/rev";
    private const string CoreDir = "C:/mods/core";

    private static ProjectLayer Layer(int rank, string name, string dir, IconProjectSettings? icons = null)
    {
        return new ProjectLayer(rank, name, [dir + "/data/xml"], [], [], [dir + "/data/art"], null,
            dir + "/mod.pgproj") { Icons = icons };
    }

    private static WorkspaceConfiguration Workspace(params ProjectLayer[] layers)
    {
        return WorkspaceConfiguration.Empty with { Layers = layers };
    }

    /// <summary>The EaWX shape exactly: two projects, neither declaring an icons node.</summary>
    [Fact]
    public void EveryProjectLacksSources_NamesThemAll()
    {
        var workspace = Workspace(
            Layer(1, "Revan's Revenge", LeafDir),
            Layer(0, "EaWX Core", CoreDir));

        var gaps = IconSourceGap.ProjectsWithoutIconSources(workspace, new MockFileSystem());

        Assert.Equal(["Revan's Revenge", "EaWX Core"], gaps);
    }

    /// <summary>
    ///     A project that declares source roots is supplying icons, whether or not the folders have
    ///     anything in them yet - the setting is there and an empty folder is the author's business.
    /// </summary>
    [Fact]
    public void AProjectThatDeclaresSourceRoots_IsNotReported()
    {
        var declared = new IconProjectSettings(
            IconProjectSettings.ConventionalMegaTexture, ["data/art/textures/icons"]);
        var workspace = Workspace(
            Layer(1, "Revan's Revenge", LeafDir, declared),
            Layer(0, "EaWX Core", CoreDir));

        Assert.Equal(["EaWX Core"],
            IconSourceGap.ProjectsWithoutIconSources(workspace, new MockFileSystem()));
    }

    /// <summary>
    ///     A mod that ships the packed mega texture at the conventional path needs no icons node -
    ///     that is what the default exists for, and nagging it would be wrong.
    /// </summary>
    [Fact]
    public void AProjectWithTheConventionalMegaTexture_IsNotReported()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{CoreDir}/data/art/textures/mt_commandbar.mtd", new MockFileData("d"));
        fs.AddFile($"{CoreDir}/data/art/textures/mt_commandbar.tga", new MockFileData("t"));

        var workspace = Workspace(
            Layer(1, "Revan's Revenge", LeafDir),
            Layer(0, "EaWX Core", CoreDir));

        Assert.Equal(["Revan's Revenge"], IconSourceGap.ProjectsWithoutIconSources(workspace, fs));
    }

    /// <summary>Half a mega texture is not one: the pair is what gets read.</summary>
    [Fact]
    public void AProjectWithOnlyTheMtd_IsStillReported()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{CoreDir}/data/art/textures/mt_commandbar.mtd", new MockFileData("d"));

        var workspace = Workspace(Layer(0, "EaWX Core", CoreDir));

        Assert.Equal(["EaWX Core"], IconSourceGap.ProjectsWithoutIconSources(fs: fs, workspace: workspace));
    }

    /// <summary>
    ///     An ordinary single-project mod that has everything set up reports nothing, because there
    ///     is nothing to report. A notice that fires on healthy workspaces stops being read.
    /// </summary>
    [Fact]
    public void AWorkspaceThatIsSetUp_ReportsNothing()
    {
        var declared = new IconProjectSettings(
            IconProjectSettings.ConventionalMegaTexture, ["data/art/textures/icons"]);

        Assert.Empty(IconSourceGap.ProjectsWithoutIconSources(
            Workspace(Layer(0, "Mod", LeafDir, declared)), new MockFileSystem()));
    }

    /// <summary>
    ///     No resolved workspace, or a layer with no project file, is a heuristic scan rather than a
    ///     misconfigured project - there is no <c>.pgproj</c> to add an icons node to.
    /// </summary>
    [Fact]
    public void NoWorkspace_ReportsNothing()
    {
        Assert.Empty(IconSourceGap.ProjectsWithoutIconSources(null, new MockFileSystem()));
    }

    [Fact]
    public void ALayerWithNoProjectFile_IsNotReported()
    {
        var heuristic = new ProjectLayer(0, "workspace", [LeafDir], [], [], [], null);

        Assert.Empty(IconSourceGap.ProjectsWithoutIconSources(
            Workspace(heuristic), new MockFileSystem()));
    }
}
