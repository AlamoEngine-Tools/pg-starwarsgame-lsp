// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Telling a model apart from a particle system.
/// </summary>
/// <remarks>
///     Both ship as <c>.alo</c>, so the extension says nothing about which reader applies. The caller
///     has to know before it picks one: handing a particle system to the model reader produces a
///     format error rather than the useful answer "this is a particle system".
/// </remarks>
public sealed class AloFileTest
{
    private static byte[] Root(uint type)
    {
        return AloChunkFixture.Chunk(type, true, []);
    }

    [Fact]
    public void Classify_RecognisesAModel()
    {
        Assert.Equal(AloFileKind.Model, AloFile.Classify(Root(0x200)));
    }

    [Fact]
    public void Classify_RecognisesAParticleSystem()
    {
        Assert.Equal(AloFileKind.Particle, AloFile.Classify(Root(0x900)));
    }

    [Fact]
    public void Classify_CallsAVersionTwoSystemAParticleSystem()
    {
        // The reader refuses v2, but it is still a particle system, and "unsupported particle system"
        // is a far better message than "this is not a model".
        Assert.Equal(AloFileKind.Particle, AloFile.Classify(Root(0x1500)));
    }

    [Fact]
    public void Classify_RejectsSomethingElseEntirely()
    {
        Assert.Equal(AloFileKind.Unknown, AloFile.Classify(Root(0x1234)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Classify_RejectsABufferTooShortToHoldARootChunk(int length)
    {
        Assert.Equal(AloFileKind.Unknown, AloFile.Classify(new byte[length]));
    }
}
