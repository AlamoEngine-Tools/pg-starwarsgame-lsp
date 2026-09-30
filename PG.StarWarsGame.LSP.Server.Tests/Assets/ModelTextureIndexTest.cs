// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Tests.Assets;

/// <summary>
///     The reader behind the model-texture diagnostic.
/// </summary>
/// <remarks>
///     What matters most here is what it does when it CANNOT read something. This runs inside XML
///     validation, on files a modder is halfway through replacing, so a throw would take the whole
///     diagnostics pass down and a guess would report a texture missing that is not.
/// </remarks>
public sealed class ModelTextureIndexTest
{
    private static ModelTextureIndex Sut(IGameAssetResolver resolver, GameIndex? index = null)
    {
        return new ModelTextureIndex(
            resolver, new FakeGameIndexService(index ?? GameIndex.Empty),
            NullLogger<ModelTextureIndex>.Instance);
    }

    /// <summary>
    ///     A model the catalog knows is answered from it, WITHOUT opening the file.
    /// </summary>
    /// <remarks>
    ///     This is the whole point of the catalog. MEASURED: parsing on demand cost one prop file
    ///     17.2s of its 17.5s. The resolver here throws if asked, so a regression that bypasses
    ///     the catalog fails loudly instead of quietly getting slow again.
    /// </remarks>
    [Fact]
    public void ModelInTheCatalog_IsAnsweredWithoutOpeningTheFile()
    {
        var index = GameIndex.Empty with
        {
            ModelTextures = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("ev_speeder.alo", ["hull.tga"])
        };

        Assert.Equal(["hull.tga"], Sut(new ThrowingResolver(), index).TexturesOf("ev_speeder.alo"));
    }

    /// <summary>
    ///     A model the catalog knows names NO textures is answered as such, again without opening
    ///     it - the empty list is a real answer, and treating it as a miss would put the cost back.
    /// </summary>
    [Fact]
    public void ModelInTheCatalogWithNoTextures_IsAnsweredEmptyWithoutOpeningTheFile()
    {
        var index = GameIndex.Empty with
        {
            ModelTextures = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("bare.alo", [])
        };

        Assert.Empty(Sut(new ThrowingResolver(), index).TexturesOf("bare.alo"));
    }

    /// <summary>
    ///     A model the catalog has never seen must still be opened. Absent is NOT "no textures":
    ///     it may be packed in an archive nothing scanned, and reporting its textures as absent
    ///     would silently drop every diagnostic about them.
    /// </summary>
    [Fact]
    public void ModelNotInTheCatalog_FallsBackToOpeningTheFile()
    {
        var index = GameIndex.Empty with
        {
            ModelTextures = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("other.alo", ["x.tga"])
        };

        // The resolver returns nothing for this one, so the answer is empty - but the point is
        // that it was ASKED, which a throwing resolver would prove by failing the test.
        Assert.Empty(Sut(new FakeResolver(null), index).TexturesOf("unscanned.alo"));
    }

    private sealed class ThrowingResolver : IGameAssetResolver
    {
        public GameAssetTiers Tiers => new(1, false, false, 0);

        public GameAssetLocation? Locate(string gameRelativePath)
        {
            throw Fail();
        }

        public byte[]? Read(string gameRelativePath)
        {
            throw Fail();
        }

        private static InvalidOperationException Fail()
        {
            return new InvalidOperationException(
                "The catalog should have answered; opening the model is the cost being removed.");
        }
    }

    [Fact]
    public void ModelThatResolvesNowhere_IsEmpty()
    {
        Assert.Empty(Sut(new FakeResolver(null)).TexturesOf("gone.alo"));
    }

    [Fact]
    public void BytesThatAreNotAnAlo_AreEmptyRatherThanAThrow()
    {
        // A truncated or half-written file is a normal thing to meet in a workspace. ModelFileFormat
        // is the diagnostic that owns "this .alo is not readable"; reporting missing textures on top
        // of it would be a second, wrong answer to the same question.
        var sut = Sut(new FakeResolver([1, 2, 3, 4, 5, 6, 7, 8]));

        Assert.Empty(sut.TexturesOf("broken.alo"));
    }

    [Fact]
    public void EmptyFile_IsEmpty()
    {
        Assert.Empty(Sut(new FakeResolver([])).TexturesOf("empty.alo"));
    }

    [Fact]
    public void BlankReference_NeverTouchesTheAssetLayer()
    {
        var resolver = new FakeResolver(null);

        Assert.Empty(Sut(resolver).TexturesOf("   "));
        Assert.Equal(0, resolver.Reads);
    }

    [Fact]
    public void TheSameModelTwice_IsReadOnce()
    {
        // This runs per model reference inside validation, and one unit file names hundreds. Without
        // the cache every keystroke would re-read and re-parse the same binaries.
        var resolver = new FakeResolver([1, 2, 3, 4, 5, 6, 7, 8]);
        var sut = Sut(resolver);

        sut.TexturesOf("ship.alo");
        sut.TexturesOf("ship.alo");
        sut.TexturesOf("SHIP.ALO");

        Assert.Equal(1, resolver.Reads);
    }

    [Fact]
    public void ClearingForgets()
    {
        var resolver = new FakeResolver([1, 2, 3, 4, 5, 6, 7, 8]);
        var sut = Sut(resolver);

        sut.TexturesOf("ship.alo");
        sut.Clear();
        sut.TexturesOf("ship.alo");

        Assert.Equal(2, resolver.Reads);
    }

    /// <summary>Answers with whatever bytes the test hands it, and counts the reads.</summary>
    private sealed class FakeResolver(byte[]? bytes) : IGameAssetResolver
    {
        public int Reads { get; private set; }

        public GameAssetTiers Tiers => new(1, false, false, 0);

        public GameAssetLocation? Locate(string gameRelativePath)
        {
            return bytes is null
                ? null
                : new GameAssetLocation(gameRelativePath, gameRelativePath,
                    GameAssetTier.Workspace);
        }

        public byte[]? Read(string gameRelativePath)
        {
            Reads++;
            return bytes;
        }
    }
}