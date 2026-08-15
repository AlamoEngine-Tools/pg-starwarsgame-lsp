// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Server.ShipNames;

namespace PG.StarWarsGame.LSP.Server.Tests.ShipNames;

public sealed class ShipNameCatalogProviderTest
{
    private const string Root = @"C:\mod";

    private static byte[] NameFile(params string[] names)
    {
        return Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", names)))
            .ToArray();
    }

    private static ShipNameCatalogProvider Build(MockFileSystem fs)
    {
        return new ShipNameCatalogProvider(fs, NullLogger<ShipNameCatalogProvider>.Instance);
    }

    /// <summary>
    ///     Paths are written game-root-relative with BACKSLASHES, so they need normalising before
    ///     they can be opened on a non-Windows path model.
    /// </summary>
    [Fact]
    public void Get_ResolvesBackslashPathsAgainstTheProjectRoot()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "Data", "Star_Destroyer.txt")] = new(NameFile("Allecto", "Devastator"))
        });

        var catalog = Build(fs).Get(Root, @"Star_Destroyer, Data\Star_Destroyer.txt");

        var pool = catalog.For("Star_Destroyer");
        Assert.NotNull(pool);
        Assert.True(pool.FileFound);
        Assert.Equal(["Allecto", "Devastator"], pool.Names);
    }

    [Fact]
    public void Get_MissingFileYieldsAPoolThatSaysSo()
    {
        var catalog = Build(new MockFileSystem()).Get(Root, @"Star_Destroyer, Data\Missing.txt");

        var pool = catalog.For("Star_Destroyer");
        Assert.NotNull(pool);
        Assert.False(pool.FileFound);
        Assert.Empty(pool.Names);
    }

    [Fact]
    public void Get_EmptyOrAbsentTagYieldsAnEmptyCatalog()
    {
        var provider = Build(new MockFileSystem());

        Assert.Empty(provider.Get(Root, null).Pools);
        Assert.Empty(provider.Get(Root, "   ").Pools);
    }

    [Fact]
    public void Get_NoProjectRootYieldsAnEmptyCatalog()
    {
        Assert.Empty(Build(new MockFileSystem()).Get("", @"Star_Destroyer, Data\SD.txt").Pools);
    }

    /// <summary>
    ///     Diagnostics and previews run per keystroke, so the files must be read once, not once per
    ///     request.
    /// </summary>
    [Fact]
    public void Get_SameWiringTwiceReturnsTheCachedCatalog()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "Data", "SD.txt")] = new(NameFile("Allecto"))
        });
        var provider = Build(fs);

        var first = provider.Get(Root, @"Star_Destroyer, Data\SD.txt");
        var second = provider.Get(Root, @"Star_Destroyer, Data\SD.txt");

        Assert.Same(first, second);
    }

    /// <summary>
    ///     ...but editing the wiring must not keep serving the old pools.
    /// </summary>
    [Fact]
    public void Get_ChangedWiringRebuilds()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "Data", "SD.txt")] = new(NameFile("Allecto")),
            [Path.Combine(Root, "Data", "CC.txt")] = new(NameFile("Home One"))
        });
        var provider = Build(fs);

        provider.Get(Root, @"Star_Destroyer, Data\SD.txt");
        var after = provider.Get(Root, @"Star_Destroyer, Data\SD.txt, Calamari_Cruiser, Data\CC.txt");

        Assert.Equal(["Home One"], after.For("Calamari_Cruiser")!.Names);
    }
}
