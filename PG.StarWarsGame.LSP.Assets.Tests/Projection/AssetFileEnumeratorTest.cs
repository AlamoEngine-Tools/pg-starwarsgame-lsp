// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

public sealed class AssetFileEnumeratorTest
{
    [Fact]
    public void Enumerate_CollectsAssetExtensions_NormalisedRelativeToRoot()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\Game\Data\Art\Textures\Foo.tga"] = new(""),
            [@"C:\Game\Data\Art\Textures\Bar.DDS"] = new(""),
            [@"C:\Game\Data\Art\Models\Baz.alo"] = new(""),
            [@"C:\Game\Data\Audio\Hit.wav"] = new(""),
            [@"C:\Game\Data\Audio\Theme.mp3"] = new(""),
            [@"C:\Game\Data\Maps\Skirmish.ted"] = new(""),
            [@"C:\Game\Data\XML\GameObjectFiles.xml"] = new(""),
            [@"C:\Game\readme.txt"] = new("")
        });

        var result = AssetFileEnumerator.Enumerate(fs, @"C:\Game");

        Assert.Contains("data/art/textures/foo.tga", result);
        Assert.Contains("data/art/textures/bar.dds", result);
        Assert.Contains("data/art/models/baz.alo", result);
        Assert.Contains("data/audio/hit.wav", result);
        Assert.Contains("data/audio/theme.mp3", result);
        Assert.Contains("data/maps/skirmish.ted", result);
        Assert.DoesNotContain("data/xml/gameobjectfiles.xml", result);
        Assert.DoesNotContain("readme.txt", result);
        Assert.Equal(6, result.Count);
    }

    [Fact]
    public void Enumerate_MissingRoot_ReturnsEmpty()
    {
        var fs = new MockFileSystem();
        var result = AssetFileEnumerator.Enumerate(fs, @"C:\DoesNotExist");
        Assert.Empty(result);
    }

    [Fact]
    public void Enumerate_IsCaseInsensitive()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\Game\Data\Art\Textures\Foo.tga"] = new("")
        });

        var result = AssetFileEnumerator.Enumerate(fs, @"C:\Game");

        Assert.Contains("DATA/ART/TEXTURES/FOO.TGA", result);
    }

    [Fact]
    public void Enumerate_FilesOutsideDataDirectory_Excluded()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\Game\Data\Art\Textures\Valid.tga"]  = new(""),
            [@"C:\Game\Mods\MyMod\Art\Unit.tga"]      = new(""),   // Mods/ not Data/
            [@"C:\Game\Corrupt.alo"]                   = new(""),   // root-level
            [@"C:\Game\Tools\Preview.dds"]             = new("")    // Tools/ not Data/
        });

        var result = AssetFileEnumerator.Enumerate(fs, @"C:\Game");

        Assert.Contains("data/art/textures/valid.tga", result);
        Assert.DoesNotContain("mods/mymod/art/unit.tga", result);
        Assert.DoesNotContain("corrupt.alo", result);
        Assert.DoesNotContain("tools/preview.dds", result);
        Assert.Single(result);
    }
}
