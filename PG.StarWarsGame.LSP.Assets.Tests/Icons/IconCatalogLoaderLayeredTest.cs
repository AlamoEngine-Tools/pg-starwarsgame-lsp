// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Project;
using Rectangle = System.Drawing.Rectangle;
using Rgba = PG.StarWarsGame.LSP.Assets.Tests.Icons.MegaTextureFixture.Rgba;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

/// <summary>
///     Icons come from every project in the workspace, not only the one the window is open on.
/// </summary>
/// <remarks>
///     A leaf mod reached its dependency's icons through nothing at all: the catalog was built from
///     one root and the ROOT project's settings, so a dependency's mega texture and its loose icon
///     sources were invisible and Core-shipped art fell through to the baked baseline. EaWX is the
///     case that matters - it ships no <c>.mtd</c> anywhere, only loose <c>.tga</c>, in the core
///     project.
///     <para>
///         A mega texture REPLACES rather than merges, so the highest-ranked layer that ships one
///         wins outright and no second atlas is consulted. A build pipeline that collects
///         dependency icons into the leaf's atlas therefore keeps working unchanged. Loose sources
///         are different: they are individual files, so they layer by name like every other
///         resource, leaf first.
///     </para>
/// </remarks>
public sealed class IconCatalogLoaderLayeredTest
{
    private const string Leaf = @"C:\mods\rev";
    private const string Core = @"C:\mods\data";

    private static IconPack EmptyBaseline()
    {
        return new IconPack(
            ImmutableDictionary<string, byte[]>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
            "hash", DateTimeOffset.UnixEpoch);
    }

    private static byte[] SolidPng(byte r, byte g, byte b)
    {
        var pixels = new byte[4 * 4 * 4];
        for (var i = 0; i < 16; i++)
        {
            pixels[i * 4] = r;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = b;
            pixels[i * 4 + 3] = 255;
        }

        return PngWriter.Write(4, 4, pixels);
    }

    private static void AddMegaTexture(MockFileSystem fileSystem, string root, string iconName, Rgba colour)
    {
        fileSystem.AddFile($@"{root}\data\art\textures\mt_mod.mtd",
            new MockFileData(MegaTextureFixture.BuildMtd((iconName, new Rectangle(0, 0, 4, 4), true))));
        fileSystem.AddFile($@"{root}\data\art\textures\mt_mod.tga",
            new MockFileData(MegaTextureFixture.BuildTga(4, 4, (_, _) => colour)));
    }

    private static IconProjectSettings Settings()
    {
        return new IconProjectSettings("data/art/textures/mt_mod", ["data/art/textures/icons"]);
    }

    private static IconCatalog Load(MockFileSystem fileSystem, params IconLayer[] layers)
    {
        return IconCatalogLoader.Load(
            fileSystem, MegaTextureFixture.CreateMtdService(fileSystem), layers, EmptyBaseline());
    }

    /// <summary>The case EaWX is in: no atlas anywhere, the icons loose in the DEPENDENCY.</summary>
    [Fact]
    public void ALooseIconInADependencyIsFound()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile($@"{Core}\data\art\textures\icons\i_button_core.png",
            new MockFileData(SolidPng(10, 20, 30)));

        var catalog = Load(fileSystem,
            new IconLayer(Leaf, Settings()), new IconLayer(Core, Settings()));

        Assert.NotNull(catalog.Resolve("i_button_core"));
    }

    /// <summary>A leaf's own loose icon wins over a dependency's of the same name.</summary>
    [Fact]
    public void TheLeafsLooseIconWinsOverTheDependencys()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile($@"{Leaf}\data\art\textures\icons\shared.png",
            new MockFileData(SolidPng(255, 0, 0)));
        fileSystem.AddFile($@"{Core}\data\art\textures\icons\shared.png",
            new MockFileData(SolidPng(0, 0, 255)));

        var catalog = Load(fileSystem,
            new IconLayer(Leaf, Settings()), new IconLayer(Core, Settings()));

        var resolved = catalog.Resolve("shared");
        Assert.NotNull(resolved);
        // Byte-for-byte: the two sources differ only in colour, so nothing weaker distinguishes them.
        Assert.Equal(SolidPng(255, 0, 0), resolved!.Png);
    }

    /// <summary>
    ///     A dependency's mega texture answers when the leaf ships none - "look further when the
    ///     leaf declares nothing".
    /// </summary>
    [Fact]
    public void ADependencysMegaTextureIsUsedWhenTheLeafShipsNone()
    {
        var fileSystem = new MockFileSystem();
        AddMegaTexture(fileSystem, Core, "I_FROM_CORE.TGA", new Rgba(1, 2, 3, 255));

        var catalog = Load(fileSystem,
            new IconLayer(Leaf, Settings()), new IconLayer(Core, Settings()));

        Assert.NotNull(catalog.Resolve("I_FROM_CORE.TGA"));
    }

    /// <summary>
    ///     The leaf's atlas REPLACES rather than merges, so a dependency's atlas is not consulted at
    ///     all once the leaf ships one. This is what keeps a build pipeline that packs dependency
    ///     icons into the leaf's atlas authoritative.
    /// </summary>
    [Fact]
    public void TheLeafsMegaTextureReplacesTheDependencys()
    {
        var fileSystem = new MockFileSystem();
        AddMegaTexture(fileSystem, Leaf, "I_FROM_LEAF.TGA", new Rgba(9, 9, 9, 255));
        AddMegaTexture(fileSystem, Core, "I_FROM_CORE.TGA", new Rgba(1, 2, 3, 255));

        var catalog = Load(fileSystem,
            new IconLayer(Leaf, Settings()), new IconLayer(Core, Settings()));

        Assert.NotNull(catalog.Resolve("I_FROM_LEAF.TGA"));
        Assert.Null(catalog.Resolve("I_FROM_CORE.TGA"));
    }

    /// <summary>A single-project workspace behaves exactly as it did before layering existed.</summary>
    [Fact]
    public void ASingleLayerBehavesAsBefore()
    {
        var fileSystem = new MockFileSystem();
        AddMegaTexture(fileSystem, Leaf, "I_ONLY.TGA", new Rgba(4, 5, 6, 255));

        var catalog = Load(fileSystem, new IconLayer(Leaf, Settings()));

        Assert.NotNull(catalog.Resolve("I_ONLY.TGA"));
    }
}