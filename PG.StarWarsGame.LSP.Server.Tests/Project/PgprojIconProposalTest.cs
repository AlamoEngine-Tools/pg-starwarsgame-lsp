// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

/// <summary>
///     The <c>icons</c> node the migration diff offers to add, when the project has icon sources on
///     disk and no way configured to reach them.
/// </summary>
/// <remarks>
///     A project with loose icons and no <c>icons</c> node draws base-game artwork everywhere, and
///     the setting that fixes it is invisible - nothing in the editor ever mentions it. Proposing it
///     inside the migration diff puts the exact lines in front of the author at the one moment they
///     are already being asked to change the file, and it stays a proposal: nothing is inferred at
///     runtime, and the change needs a yes.
///     <para>
///         Deliberately NOT an <c>IDocumentMigration</c>. Those run over the raw tree with no path
///         and no filesystem, by design, so a step cannot tell whether the folder it would name
///         actually exists - and a node naming a folder that is not there is worse than no node.
///     </para>
/// </remarks>
public sealed class PgprojIconProposalTest
{
    private const string ProjectDir = "C:/mods/rev";

    private static JsonObject Project(params string[] artDirs)
    {
        var art = new JsonArray();
        foreach (var dir in artDirs) art.Add(dir);

        return new JsonObject
        {
            ["name"] = "Revan's Revenge",
            ["directories"] = new JsonObject { ["xml"] = new JsonArray { "data/xml" }, ["art"] = art }
        };
    }

    private static MockFileSystem WithIcons(params string[] folders)
    {
        var fs = new MockFileSystem();
        foreach (var folder in folders)
            fs.AddFile($"{ProjectDir}/{folder}/I_BUTTON_DP2.TGA", new MockFileData([1, 2, 3]));
        return fs;
    }

    private static string? Propose(JsonObject document, MockFileSystem fs)
    {
        return PgprojIconProposal.TryPropose(document, ProjectDir, fs);
    }

    [Fact]
    public void AProjectWithLooseIconsAndNoNode_GetsOneProposed()
    {
        var document = Project("data/art");
        var notice = Propose(document, WithIcons("data/art/textures/icons"));

        Assert.NotNull(notice);
        var roots = document["icons"]!["sourceRoots"]!.AsArray();
        Assert.Equal("data/art/textures/icons", roots[0]!.GetValue<string>());
    }

    /// <summary>
    ///     The notice is the only thing the author reads before deciding, so it has to say what the
    ///     lines do rather than just that something changed.
    /// </summary>
    [Fact]
    public void TheNotice_SaysWhatTheNodeIsFor()
    {
        var notice = Propose(Project("data/art"), WithIcons("data/art/textures/icons"));

        Assert.Contains("icon", notice!, StringComparison.OrdinalIgnoreCase);
        // ASCII only - this is user-facing text.
        Assert.Matches("^[\\x20-\\x7e]+$", notice);
    }

    /// <summary>
    ///     The paths are relative to THIS project, and two projects in one workspace legitimately
    ///     nest their art differently - the EaWX pair does exactly that.
    /// </summary>
    [Fact]
    public void ThePathsAreRelativeToTheProject()
    {
        var document = Project("art");
        Propose(document, WithIcons("art/textures/icons"));

        Assert.Equal("art/textures/icons",
            document["icons"]!["sourceRoots"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public void EveryArtDirectoryWithIcons_IsProposed()
    {
        var document = Project("data/art", "data/art_extra");
        Propose(document, WithIcons("data/art/textures/icons", "data/art_extra/textures/icons"));

        var roots = document["icons"]!["sourceRoots"]!.AsArray();
        Assert.Equal(2, roots.Count);
    }

    /// <summary>A setting the author already made is never second-guessed.</summary>
    [Fact]
    public void AProjectThatAlreadyDeclaresIcons_IsLeftAlone()
    {
        var document = Project("data/art");
        document["icons"] = new JsonObject { ["sourceRoots"] = new JsonArray { "somewhere/else" } };

        Assert.Null(Propose(document, WithIcons("data/art/textures/icons")));
        Assert.Equal("somewhere/else",
            document["icons"]!["sourceRoots"]!.AsArray()[0]!.GetValue<string>());
    }

    /// <summary>
    ///     A mod that ships the packed atlas needs nothing: that is what the convention is for, and
    ///     proposing source folders to it would be noise.
    /// </summary>
    [Fact]
    public void AProjectWithTheConventionalMegaTexture_IsLeftAlone()
    {
        var fs = WithIcons("data/art/textures/icons");
        fs.AddFile($"{ProjectDir}/data/art/textures/mt_commandbar.mtd", new MockFileData([1]));
        fs.AddFile($"{ProjectDir}/data/art/textures/mt_commandbar.tga", new MockFileData([1]));

        Assert.Null(Propose(Project("data/art"), fs));
    }

    /// <summary>
    ///     The whole safety property: a guessed folder that is not there is never written into
    ///     somebody's project file.
    /// </summary>
    [Fact]
    public void NoIconsFolderOnDisk_ProposesNothing()
    {
        var document = Project("data/art");

        Assert.Null(Propose(document, new MockFileSystem()));
        Assert.Null(document["icons"]);
    }

    /// <summary>An empty scaffold folder is not a set of sources.</summary>
    [Fact]
    public void AnEmptyIconsFolder_ProposesNothing()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory($"{ProjectDir}/data/art/textures/icons");

        Assert.Null(Propose(Project("data/art"), fs));
    }

    [Fact]
    public void AProjectThatDeclaresNoArtDirectories_ProposesNothing()
    {
        var document = new JsonObject { ["name"] = "Mod" };

        Assert.Null(Propose(document, WithIcons("data/art/textures/icons")));
    }
}
