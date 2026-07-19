// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

public sealed class BoneNameExtractorTest
{
    [Fact]
    public void Extract_MissingRoot_ReturnsEmpty()
    {
        var fs = new MockFileSystem();
        var result = BoneNameExtractor.Extract(fs, @"C:\DoesNotExist", _ => ["root"]);
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_NoAloFiles_ReturnsEmpty()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\Game\Data\Art\Textures\Foo.tga"] = new("")
        });

        var result = BoneNameExtractor.Extract(fs, @"C:\Game", _ => ["root"]);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_AloFile_ReturnsBonesKeyedByFilename()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\Game\Data\Art\Models\Unit.alo"] = new("alo-bytes")
        });

        var result = BoneNameExtractor.Extract(fs, @"C:\Game", _ => ["root", "turret_bone"]);

        // Keyed by bare filename (ModelBoneKey), matching how XML references models and how the
        // bone catalog is looked up.
        Assert.True(result.ContainsKey("unit.alo"));
        Assert.Equal(["root", "turret_bone"], result["unit.alo"]);
    }

    [Fact]
    public void Extract_UnreadableAloFile_SkippedWithoutThrowing()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\Game\Data\Art\Models\Broken.alo"] = new("garbage")
        });

        var result = BoneNameExtractor.Extract(fs, @"C:\Game",
            _ => throw new InvalidOperationException("unreadable ALO"));

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_AloFileWithNoBones_OmittedFromResult()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\Game\Data\Art\Models\Empty.alo"] = new("alo-bytes")
        });

        var result = BoneNameExtractor.Extract(fs, @"C:\Game", _ => null);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_DefaultLoader_NoAloFiles_ReturnsEmpty()
    {
        // The IFileSystem-only convenience overload must compile and return empty when no .alo
        // files exist, exercising the production ALO-loading path without a valid binary fixture.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\Game\Data\Art\Textures\Foo.tga"] = new("")
        });

        var result = BoneNameExtractor.Extract(fs, @"C:\Game");

        Assert.Empty(result);
    }
}