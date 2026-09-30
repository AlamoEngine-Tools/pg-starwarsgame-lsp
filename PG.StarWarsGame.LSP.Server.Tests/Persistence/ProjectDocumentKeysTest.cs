// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Persistence;
using PG.StarWarsGame.LSP.Server.Tests.Story;

namespace PG.StarWarsGame.LSP.Server.Tests.Persistence;

/// <summary>
///     The one place that turns a document into the key persisted files name it by. Everything else
///     asks it rather than folding a path for itself.
/// </summary>
public sealed class ProjectDocumentKeysTest
{
    private const string ProjectDir = "C:/mods/mymod";
    private const string Pgproj = ProjectDir + "/mod.pgproj";

    private static ProjectDocumentKeys Build(MockFileSystem? fs = null, bool withProject = true)
    {
        var layers = withProject
            ? new[]
            {
                new ProjectLayer(1, "Mod", [ProjectDir + "/data/xml"], [], [], [], null, Pgproj)
            }
            : [];
        return new ProjectDocumentKeys(
            new StoryCommandTestFixtures.StubReloadService(
                WorkspaceConfiguration.Empty with { Layers = layers }),
            new FileHelper(fs ?? new MockFileSystem()));
    }

    // ── keys from a document ─────────────────────────────────────────────────

    [Fact]
    public void NodeKey_IsTheThreadAndEventHashedTogether()
    {
        Assert.Equal(
            DocumentKey.Composite("data/xml/story_main.xml", "Start"),
            Build().NodeKey(ProjectDir + "/data/xml/story_main.xml", "Start"));
    }

    // Two events in one thread are two nodes; one thread's key would name neither.
    [Fact]
    public void NodeKey_DiffersPerEvent()
    {
        var keys = Build();

        Assert.NotEqual(
            keys.NodeKey(ProjectDir + "/data/xml/story_main.xml", "Start"),
            keys.NodeKey(ProjectDir + "/data/xml/story_main.xml", "Next"));
    }

    // Campaign and faction are a composite too, so they are hashed together rather than joined.
    [Fact]
    public void GraphKey_IsTheCampaignAndFactionHashedTogether()
    {
        Assert.Equal(DocumentKey.Composite("GC", "Rebel"), ProjectDocumentKeys.GraphKey("GC", "Rebel"));
        Assert.NotEqual(ProjectDocumentKeys.GraphKey("GC", "Rebel"), ProjectDocumentKeys.LegacyGraphKey("GC"));
    }

    // The server holds documents as lowercased absolute file:// URIs. The fold is what makes a key
    // derived from one identical to a key derived from the real path.
    [Fact]
    public void NodeKey_FileUriAndPath_AgreeDespiteCase()
    {
        var keys = Build();

        Assert.Equal(
            keys.NodeKey("file:///c:/mods/mymod/data/xml/story_main.xml", "Start"),
            keys.NodeKey("C:/mods/MyMod/Data/XML/Story_Main.xml", "START"));
    }

    [Fact]
    public void NodeKey_OutsideTheProject_IsNull()
    {
        Assert.Null(Build().NodeKey("C:/elsewhere/story_main.xml", "Start"));
    }

    [Fact]
    public void NodeKey_WithoutAProject_IsNull()
    {
        Assert.Null(Build(withProject: false).NodeKey(ProjectDir + "/data/xml/story_main.xml", "Start"));
    }

    // ── keys from a base name, for the migration ─────────────────────────────

    // The layout sidecar named threads by base name alone, so bringing it forward means finding
    // which file that was. Searching the project's xml roots is what makes that answerable at all.
    [Fact]
    public void NodeKeyForBaseName_UniqueMatch_ResolvesToItsPath()
    {
        var fs = new MockFileSystem();
        fs.AddFile(ProjectDir + "/data/xml/story/story_main.xml", new MockFileData("<Story/>"));

        Assert.Equal(
            DocumentKey.Composite("data/xml/story/story_main.xml", "Start"),
            Build(fs).NodeKeyForBaseName("story_main.xml", "Start"));
    }

    [Fact]
    public void NodeKeyForBaseName_IsCaseInsensitive()
    {
        var fs = new MockFileSystem();
        fs.AddFile(ProjectDir + "/data/xml/Story_Main.xml", new MockFileData("<Story/>"));

        Assert.Equal(
            DocumentKey.Composite("data/xml/Story_Main.xml", "Start"),
            Build(fs).NodeKeyForBaseName("story_main.xml", "Start"));
    }

    // Two threads of the same name in different directories are exactly what the base name could
    // never distinguish. Guessing one would move somebody's nodes onto the wrong graph, so the
    // entry is dropped instead and the user is told how many went.
    [Fact]
    public void NodeKeyForBaseName_Ambiguous_IsNull()
    {
        var fs = new MockFileSystem();
        fs.AddFile(ProjectDir + "/data/xml/story/story_main.xml", new MockFileData("<Story/>"));
        fs.AddFile(ProjectDir + "/data/xml/campaign/story_main.xml", new MockFileData("<Story/>"));

        Assert.Null(Build(fs).NodeKeyForBaseName("story_main.xml", "Start"));
    }

    [Fact]
    public void NodeKeyForBaseName_NoSuchFile_IsNull()
    {
        Assert.Null(Build().NodeKeyForBaseName("story_main.xml", "Start"));
    }

    // ── documents in a referenced project ────────────────────────────────────

    private const string CoreDir = "C:/mods/core";
    private const string CorePgproj = CoreDir + "/core.pgproj";

    /// <summary>A leaf over a dependency, leaf ranked higher.</summary>
    private static ProjectDocumentKeys BuildLayered()
    {
        ProjectLayer[] layers =
        [
            new(1, "Mod", [ProjectDir + "/data/xml"], [], [], [], null, Pgproj),
            new(0, "Core", [CoreDir + "/data/xml"], [], [], [], null, CorePgproj)
        ];
        return new ProjectDocumentKeys(
            new StoryCommandTestFixtures.StubReloadService(
                WorkspaceConfiguration.Empty with { Layers = layers }),
            new FileHelper(new MockFileSystem()));
    }

    /// <summary>
    ///     A thread that lives in a REFERENCED project still has a key.
    /// </summary>
    /// <remarks>
    ///     Every key was taken relative to the root project alone, and a path outside it resolved to
    ///     null - so a node in a dependency had no key, its position was silently not saved, and the
    ///     graph re-laid-out on every open. That is the lag the maintainer reported.
    /// </remarks>
    [Fact]
    public void NodeKey_ForADocumentInAReferencedProject_IsNotNull()
    {
        Assert.NotNull(BuildLayered().NodeKey(CoreDir + "/data/xml/core_story.xml", "Start"));
    }

    /// <summary>
    ///     It is keyed against the layer the file LIVES in, so the same dependency thread keys the
    ///     same whichever leaf is open - four EaWX mods share one core project, and a layout the
    ///     author arranged through one must not be a different graph through another.
    /// </summary>
    [Fact]
    public void NodeKey_ForADependencyDocument_IsRelativeToThatDependency()
    {
        Assert.Equal(
            DocumentKey.Composite("data/xml/core_story.xml", "Start"),
            BuildLayered().NodeKey(CoreDir + "/data/xml/core_story.xml", "Start"));
    }

    /// <summary>
    ///     The root project's own documents keep the key they always had, so no layout saved before
    ///     this change is orphaned.
    /// </summary>
    [Fact]
    public void NodeKey_ForARootDocument_IsUnchanged()
    {
        Assert.Equal(
            DocumentKey.Composite("data/xml/story_main.xml", "Start"),
            BuildLayered().NodeKey(ProjectDir + "/data/xml/story_main.xml", "Start"));
    }

    /// <summary>
    ///     When one project's directory CONTAINS another's, the innermost wins. Otherwise a leaf
    ///     nested inside its dependency would key its own files relative to the dependency and every
    ///     existing layout in it would be orphaned.
    /// </summary>
    [Fact]
    public void NodeKey_WithANestedProject_UsesTheInnermost()
    {
        ProjectLayer[] layers =
        [
            new(1, "Leaf", ["C:/mods/outer/leaf/data/xml"], [], [], [], null,
                "C:/mods/outer/leaf/leaf.pgproj"),
            new(0, "Outer", ["C:/mods/outer/data/xml"], [], [], [], null, "C:/mods/outer/outer.pgproj")
        ];
        var keys = new ProjectDocumentKeys(
            new StoryCommandTestFixtures.StubReloadService(
                WorkspaceConfiguration.Empty with { Layers = layers }),
            new FileHelper(new MockFileSystem()));

        Assert.Equal(
            DocumentKey.Composite("data/xml/t.xml", "Start"),
            keys.NodeKey("C:/mods/outer/leaf/data/xml/t.xml", "Start"));
    }

    /// <summary>A document under no project at all still has no key.</summary>
    [Fact]
    public void NodeKey_ForADocumentOutsideEveryProject_IsNull()
    {
        Assert.Null(BuildLayered().NodeKey("C:/elsewhere/data/xml/stray.xml", "Start"));
    }
}