// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Workspace;

/// <summary>
///     The one rule for game-relative paths (#310): slashes go forward, a relative path carries no
///     leading or trailing slash, a key is lower case, and a comparison ignores case and the slash
///     the author did or did not type.
/// </summary>
public sealed class GamePathsTest
{
    [Theory]
    [InlineData(@"Data\XML\Foo.xml", "Data/XML/Foo.xml")]
    [InlineData("Data/XML/Foo.xml", "Data/XML/Foo.xml")]
    [InlineData(@"Data/XML\Foo.xml", "Data/XML/Foo.xml")]
    [InlineData(@"\Data\XML\", "Data/XML")]
    [InlineData("/Data/XML/", "Data/XML")]
    [InlineData("", "")]
    [InlineData("/", "")]
    public void Normalize_FoldsSlashesForwardAndDropsTheOuterOnes_KeepingCase(string raw, string expected)
    {
        Assert.Equal(expected, GamePaths.Normalize(raw));
    }

    [Theory]
    [InlineData(@"C:\Games\FoC\Data", "C:/Games/FoC/Data")]
    [InlineData("/home/x/game/", "/home/x/game/")]
    [InlineData(@"\\server\share\x", "//server/share/x")]
    public void Forward_FoldsSlashesOnly_SoAnAbsolutePathKeepsItsRoot(string raw, string expected)
    {
        Assert.Equal(expected, GamePaths.Forward(raw));
    }

    [Theory]
    [InlineData(@"Data\XML\Foo.xml", "data/xml/foo.xml")]
    [InlineData("/DATA/XML/", "data/xml")]
    public void Key_IsTheNormalizedPathInLowerCase(string raw, string expected)
    {
        Assert.Equal(expected, GamePaths.Key(raw));
    }

    [Theory]
    [InlineData(@"Data\XML\Foo.xml", "data/xml/foo.xml", true)]
    [InlineData("Data/XML/", "data\\xml", true)]
    [InlineData("Data/XML/Foo.xml", "Data/XML/Bar.xml", false)]
    [InlineData("Data/XML", "Data/XMLx", false)]
    [InlineData("", "", true)]
    public void Same_IgnoresCaseSlashesAndTheOuterSlash(string a, string b, bool expected)
    {
        Assert.Equal(expected, GamePaths.Same(a, b));
    }

    [Theory]
    [InlineData("Data/XML/a.xml", @"data\xml\", true)]
    [InlineData("Data/XML/Units/a.xml", "Data/XML", true)]
    [InlineData("Data/XML", "Data/XML", true)]
    [InlineData("Data/XMLx/a.xml", "Data/XML", false)]
    [InlineData("Data/Art/a.alo", "Data/XML", false)]
    [InlineData("Data", "Data/XML", false)]
    [InlineData("Data/XML/a.xml", "", true)]
    public void Under_IsSegmentAware_AndIgnoresCase(string path, string root, bool expected)
    {
        Assert.Equal(expected, GamePaths.Under(path, root));
    }
}