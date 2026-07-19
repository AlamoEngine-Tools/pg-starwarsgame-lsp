// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class ModelBoneKeyTest
{
    [Theory]
    [InlineData("UB_PALACE.ALO", "ub_palace.alo")]
    [InlineData("ub_palace.alo", "ub_palace.alo")]
    [InlineData("data/art/models/ub_palace.alo", "ub_palace.alo")]
    [InlineData(@"DATA\ART\MODELS\UB_PALACE.ALO", "ub_palace.alo")]
    [InlineData("  UB_Palace.alo  ", "ub_palace.alo")]
    [InlineData("Models/Sub/Foo.ALO", "foo.alo")]
    public void From_ReducesAnyModelReferenceToBareLowercaseFilename(string input, string expected)
    {
        Assert.Equal(expected, ModelBoneKey.From(input));
    }

    [Fact]
    public void From_AndTheCatalogComparer_MatchAcrossCasing()
    {
        // A full-path MEG entry and the bare-filename XML reference must collapse to the same key,
        // which is the whole point: the producer and consumer sides used to disagree here.
        Assert.Equal(ModelBoneKey.From("data/art/models/UB_PALACE.ALO"), ModelBoneKey.From("ub_palace.alo"));
    }
}
