// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Workspace;

public sealed class EaWXmlContextTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static EaWXmlContext Build()
    {
        return new EaWXmlContext(new FileHelper(new MockFileSystem()));
    }

    private static string ToUri(string path)
    {
        return new FileHelper(new MockFileSystem()).PathToFileUri(path);
    }

    [Fact]
    public void IsEaWXmlFile_NoDirectoriesRegistered_ReturnsFalse()
    {
        var ctx = Build();
        var uri = ToUri(Path.Combine(Root("game"), "data", "xml", "foo.xml"));

        Assert.False(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void IsEaWXmlFile_FileInRegisteredDirectory_ReturnsTrue()
    {
        var dir = Path.Combine(Root("game"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dir);

        var uri = ToUri(Path.Combine(dir, "foo.xml"));

        Assert.True(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void IsEaWXmlFile_FileInSubdirectoryOfRegistered_ReturnsTrue()
    {
        var dir = Path.Combine(Root("game"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dir);

        var uri = ToUri(Path.Combine(dir, "sub", "foo.xml"));

        Assert.True(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void IsEaWXmlFile_FileOutsideRegisteredDirectory_ReturnsFalse()
    {
        var dir = Path.Combine(Root("game"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dir);

        var uri = ToUri(Path.Combine(Root("game"), "scripts", "foo.lua"));

        Assert.False(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void IsEaWXmlFile_SiblingDirectoryWithSharedPrefix_ReturnsFalse()
    {
        // Registered: data/xml/  - must NOT match data/xmlExtra/
        var dir = Path.Combine(Root("game"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dir);

        var uri = ToUri(Path.Combine(Root("game"), "data", "xmlExtra", "foo.xml"));

        Assert.False(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void IsEaWXmlFile_CaseInsensitive()
    {
        var dir = Path.Combine(Root("Game"), "Data", "Xml");
        var ctx = Build();
        ctx.AddDirectory(dir);

        // File URI with different casing
        var uri = ToUri(Path.Combine(Root("game"), "data", "xml", "FOO.XML"));

        Assert.True(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void AddDirectory_WithTrailingSeparator_DoesNotDoubleUp()
    {
        var dir = Path.Combine(Root("game"), "data", "xml") + Path.DirectorySeparatorChar;
        var ctx = Build();
        ctx.AddDirectory(dir);

        var uri = ToUri(Path.Combine(Root("game"), "data", "xml", "foo.xml"));

        Assert.True(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void AddDirectory_MultipleDirectories_AllChecked()
    {
        var dir1 = Path.Combine(Root("game"), "data", "xml");
        var dir2 = Path.Combine(Root("mod"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dir1);
        ctx.AddDirectory(dir2);

        Assert.True(ctx.IsEaWXmlFile(ToUri(Path.Combine(dir1, "foo.xml"))));
        Assert.True(ctx.IsEaWXmlFile(ToUri(Path.Combine(dir2, "bar.xml"))));
    }

    [Fact]
    public void IsEaWXmlFile_NonFileUri_ReturnsFalse()
    {
        var ctx = Build();

        Assert.False(ctx.IsEaWXmlFile("not-a-uri"));
    }

    // ── AI subdirectory exclusion ─────────────────────────────────────────────

    [Fact]
    public void IsEaWXmlFile_FileInAiSubdirectory_ReturnsFalse()
    {
        var dir = Path.Combine(Root("game"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dir);

        var uri = ToUri(Path.Combine(dir, "AI", "foo.xml"));

        Assert.False(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void IsEaWXmlFile_FileInNestedAiSubdirectory_ReturnsFalse()
    {
        var dir = Path.Combine(Root("game"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dir);

        var uri = ToUri(Path.Combine(dir, "sub", "AI", "foo.xml"));

        Assert.False(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void IsEaWXmlFile_DirectoryWithAiPrefix_ReturnsTrue()
    {
        // "AI_players" is not the AI subdirectory - only an exact "AI" segment is excluded.
        var dir = Path.Combine(Root("game"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dir);

        var uri = ToUri(Path.Combine(dir, "AI_players", "foo.xml"));

        Assert.True(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void AddDirectory_WithForwardSlashes_MatchesFileUri()
    {
        // AddDirectory may receive a path with forward slashes (e.g., from cross-platform config)
        var dir = Root("game").Replace(Path.DirectorySeparatorChar, '/') + "/data/xml";
        var ctx = Build();
        ctx.AddDirectory(dir);

        var uri = ToUri(Path.Combine(Root("game"), "data", "xml", "foo.xml"));

        Assert.True(ctx.IsEaWXmlFile(uri));
    }

    [Fact]
    public void AddDirectory_WithFileUri_MatchesFileUri()
    {
        // AddDirectory may receive a file:// URI (from workspace folder URIs)
        var dirUri = ToUri(Path.Combine(Root("game"), "data", "xml"));
        var ctx = Build();
        ctx.AddDirectory(dirUri);

        var uri = ToUri(Path.Combine(Root("game"), "data", "xml", "foo.xml"));

        Assert.True(ctx.IsEaWXmlFile(uri));
    }

    // ── SetDirectories ────────────────────────────────────────────────────────

    [Fact]
    public void SetDirectories_ReplacesExistingDirectories()
    {
        var dirA = Path.Combine(Root("game"), "data", "xml");
        var dirB = Path.Combine(Root("mod"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dirA);

        ctx.SetDirectories([dirB]);

        Assert.False(ctx.IsEaWXmlFile(ToUri(Path.Combine(dirA, "foo.xml"))));
        Assert.True(ctx.IsEaWXmlFile(ToUri(Path.Combine(dirB, "bar.xml"))));
    }

    [Fact]
    public void SetDirectories_EmptyList_ClearsAllDirectories()
    {
        var dir = Path.Combine(Root("game"), "data", "xml");
        var ctx = Build();
        ctx.AddDirectory(dir);

        ctx.SetDirectories([]);

        Assert.False(ctx.IsEaWXmlFile(ToUri(Path.Combine(dir, "foo.xml"))));
    }

    // ── IsLeafFile / SetLeafDirectories ───────────────────────────────────────

    [Fact]
    public void IsLeafFile_BeforeSetLeafDirectories_ReturnsFalse()
    {
        var ctx = Build();
        var uri = ToUri(Path.Combine(Root("leaf"), "data", "xml", "foo.xml"));
        Assert.False(ctx.IsLeafFile(uri));
    }

    [Fact]
    public void IsLeafFile_FileUnderLeafDirectory_ReturnsTrue()
    {
        var leafDir = Path.Combine(Root("leaf"), "data", "xml");
        var ctx = Build();
        ctx.SetLeafDirectories([leafDir]);

        var uri = ToUri(Path.Combine(leafDir, "foo.xml"));
        Assert.True(ctx.IsLeafFile(uri));
    }

    [Fact]
    public void IsLeafFile_FileInSubdirectoryOfLeaf_ReturnsTrue()
    {
        var leafDir = Path.Combine(Root("leaf"), "data", "xml");
        var ctx = Build();
        ctx.SetLeafDirectories([leafDir]);

        var uri = ToUri(Path.Combine(leafDir, "sub", "foo.xml"));
        Assert.True(ctx.IsLeafFile(uri));
    }

    [Fact]
    public void IsLeafFile_FileUnderDependencyDirectoryOnly_ReturnsFalse()
    {
        var depDir = Path.Combine(Root("dep"), "data", "xml");
        var leafDir = Path.Combine(Root("leaf"), "data", "xml");
        var ctx = Build();
        ctx.SetLeafDirectories([leafDir]);

        var uri = ToUri(Path.Combine(depDir, "foo.xml"));
        Assert.False(ctx.IsLeafFile(uri));
    }

    [Fact]
    public void SetLeafDirectories_EmptyList_ClearsLeafSet()
    {
        var leafDir = Path.Combine(Root("leaf"), "data", "xml");
        var ctx = Build();
        ctx.SetLeafDirectories([leafDir]);
        ctx.SetLeafDirectories([]);

        var uri = ToUri(Path.Combine(leafDir, "foo.xml"));
        Assert.False(ctx.IsLeafFile(uri));
    }
}