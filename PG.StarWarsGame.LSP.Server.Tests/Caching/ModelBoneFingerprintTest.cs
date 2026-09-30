// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Tests.Caching;

/// <summary>
///     The fingerprint is the ONLY thing standing between a cached bone catalog and a wrong one.
///     It is deliberately cheap - path, size and write time per <c>.alo</c>, never the bytes -
///     because hashing tens of thousands of models would cost what the cache is meant to save.
///     That trade is what these pin: everything a cheap key CAN see must move it.
/// </summary>
public sealed class ModelBoneFingerprintTest
{
    private static MockFileSystem WithModels(params (string Path, string Content)[] files)
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/mod/art/models");
        foreach (var (path, content) in files)
            fs.AddFile(path, new MockFileData(content) { LastWriteTime = new DateTime(2026, 1, 1) });
        return fs;
    }

    private static string Compute(MockFileSystem fs, params string[] roots)
    {
        return ModelBoneFingerprint.Compute(new FileHelper(fs), roots);
    }

    [Fact]
    public void Compute_IsStableAcrossCalls()
    {
        var fs = WithModels(("/mod/art/models/ev_speeder.alo", "aaa"));

        Assert.Equal(Compute(fs, "/mod/art/models"), Compute(fs, "/mod/art/models"));
    }

    [Fact]
    public void Compute_ChangesWhenAModelIsAdded()
    {
        var fs = WithModels(("/mod/art/models/ev_speeder.alo", "aaa"));
        var before = Compute(fs, "/mod/art/models");

        fs.AddFile("/mod/art/models/uv_walker.alo", new MockFileData("bbb"));

        Assert.NotEqual(before, Compute(fs, "/mod/art/models"));
    }

    [Fact]
    public void Compute_ChangesWhenAModelIsRemoved()
    {
        var fs = WithModels(
            ("/mod/art/models/ev_speeder.alo", "aaa"),
            ("/mod/art/models/uv_walker.alo", "bbb"));
        var before = Compute(fs, "/mod/art/models");

        fs.RemoveFile("/mod/art/models/uv_walker.alo");

        Assert.NotEqual(before, Compute(fs, "/mod/art/models"));
    }

    [Fact]
    public void Compute_ChangesWhenAModelChangesSize()
    {
        var fs = WithModels(("/mod/art/models/ev_speeder.alo", "aaa"));
        var before = Compute(fs, "/mod/art/models");

        fs.File.WriteAllText("/mod/art/models/ev_speeder.alo", "aaaaaaaaaaaa");

        Assert.NotEqual(before, Compute(fs, "/mod/art/models"));
    }

    [Fact]
    public void Compute_ChangesWhenAModelIsRewrittenAtTheSameSize()
    {
        // Same length, different content - only the write time separates them. A size-only key
        // would serve the old skeleton for an edited model, which is the failure that is silent.
        var fs = WithModels(("/mod/art/models/ev_speeder.alo", "aaa"));
        var before = Compute(fs, "/mod/art/models");

        fs.File.WriteAllText("/mod/art/models/ev_speeder.alo", "bbb");
        fs.File.SetLastWriteTime("/mod/art/models/ev_speeder.alo", new DateTime(2026, 6, 1));

        Assert.NotEqual(before, Compute(fs, "/mod/art/models"));
    }

    [Fact]
    public void Compute_IgnoresFilesThatAreNotModels()
    {
        // An asset root holds textures and sounds too, and they vastly outnumber the models.
        // Folding them in would rebuild the bone catalog every time a texture was touched.
        var fs = WithModels(("/mod/art/models/ev_speeder.alo", "aaa"));
        var before = Compute(fs, "/mod/art/models");

        fs.AddFile("/mod/art/models/ev_speeder.tga", new MockFileData("image"));

        Assert.Equal(before, Compute(fs, "/mod/art/models"));
    }

    [Fact]
    public void Compute_DoesNotDependOnEnumerationOrder()
    {
        // The filesystem makes no ordering promise, so an unsorted key would differ run to run on
        // the same unchanged tree and rebuild every time - a cache that never hits.
        var fs = WithModels(
            ("/mod/a/one.alo", "aaa"),
            ("/mod/b/two.alo", "bbb"));

        Assert.Equal(Compute(fs, "/mod/a", "/mod/b"), Compute(fs, "/mod/b", "/mod/a"));
    }

    [Fact]
    public void Compute_DistinguishesSameNamedModelsInDifferentRoots()
    {
        var fs = WithModels(("/mod/a/ev_speeder.alo", "aaa"), ("/mod/b/ev_speeder.alo", "aaa"));

        Assert.NotEqual(Compute(fs, "/mod/a"), Compute(fs, "/mod/a", "/mod/b"));
    }

    [Fact]
    public void Compute_MissingRoot_IsNotAnError()
    {
        Assert.False(string.IsNullOrEmpty(Compute(new MockFileSystem(), "/nope")));
    }

    [Fact]
    public void Compute_NoRoots_IsStable()
    {
        var fs = new MockFileSystem();

        Assert.Equal(Compute(fs), Compute(fs));
    }
}
