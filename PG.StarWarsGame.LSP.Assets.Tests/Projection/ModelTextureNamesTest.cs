// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

/// <summary>
///     The texture names a model carries inside itself, pulled from the bytes the bone pass has
///     already read.
/// </summary>
/// <remarks>
///     MEASURED 2026-09-30: parsing these lazily during validation was the whole of the workspace
///     sweep's cost - one prop file spent 17.2s of its 17.5s inside <c>TexturesOf</c>, while the
///     catalog lookups it was first blamed on were 30ms. Extracting them in the pass that already
///     opens every <c>.alo</c> is what makes them free.
/// </remarks>
public sealed class ModelTextureNamesTest
{
    [Fact]
    public void Read_NotAnAlo_IsEmptyRatherThanThrowing()
    {
        // The scan must never fail for one bad file; ModelFileFormat owns "this .alo is unreadable".
        Assert.Empty(ModelTextureNames.Read([1, 2, 3, 4]));
    }

    [Fact]
    public void Read_EmptyBytes_IsEmpty()
    {
        Assert.Empty(ModelTextureNames.Read([]));
    }

    [Fact]
    public void Read_ClassifiesBeforeReading()
    {
        // A particle system and a model are both .alo and are told apart by their first chunk.
        // Reading one as the other throws rather than lying, so the classify must come first -
        // this is the same rule the 3D preview follows.
        Assert.Equal(AloFileKind.Unknown, AloFile.Classify([0, 0, 0, 0]));
        Assert.Empty(ModelTextureNames.Read([0, 0, 0, 0]));
    }
}
