// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
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
    /// <summary>Answers with whatever bytes the test hands it, and counts the reads.</summary>
    private sealed class FakeResolver(byte[]? bytes) : IGameAssetResolver
    {
        public int Reads { get; private set; }

        public GameAssetTiers Tiers => new(1, false, false, 0);

        public GameAssetLocation? Locate(string gameRelativePath)
        {
            return bytes is null ? null : new GameAssetLocation(gameRelativePath, gameRelativePath,
                GameAssetTier.Workspace);
        }

        public byte[]? Read(string gameRelativePath)
        {
            Reads++;
            return bytes;
        }
    }

    private static ModelTextureIndex Sut(FakeResolver resolver)
    {
        return new ModelTextureIndex(resolver, NullLogger<ModelTextureIndex>.Instance);
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
}
