// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Diagnostics;

namespace PG.StarWarsGame.LSP.Lua.Tests.Diagnostics;

/// <summary>
///     Loretta numbers its own diagnostics <c>LUA####</c>, so the id is already there to be reused -
///     stripping the prefix gives a number that drops straight into the Syntax group.
/// </summary>
public sealed class LorettaDiagnosticIdsTest
{
    [Theory]
    [InlineData("LUA0014", 14)]
    [InlineData("LUA0018", 18)]
    [InlineData("LUA1012", 1012)]
    [InlineData("LUA0001", 1)]
    public void ALorettaId_BecomesTheSameNumberInTheSyntaxGroup(string lorettaId, int expected)
    {
        Assert.True(LorettaDiagnosticIds.TryMap(lorettaId, out var id));
        Assert.Equal(new DiagnosticId(DiagnosticGroup.Syntax, expected), id);
    }

    [Fact]
    public void TheMappedId_RendersInTheNormalWireFormat()
    {
        Assert.True(LorettaDiagnosticIds.TryMap("LUA0018", out var id));
        Assert.Equal("aetswg-012-0018", id.ToString());
    }

    [Fact]
    public void TheLorettaPrefix_IsCaseInsensitive()
    {
        Assert.True(LorettaDiagnosticIds.TryMap("lua0018", out _));
    }

    // Anything off-shape yields no id at all, which leaves the diagnostic visible but
    // unsuppressable. Throwing here would take down a whole document's diagnostics over one
    // unexpected code from a dependency we do not control.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LUA")]
    [InlineData("LUA00AB")]
    [InlineData("XYZ0014")]
    [InlineData("LUA-0014")]
    public void AnUnexpectedShape_MapsToNothing(string? lorettaId)
    {
        Assert.False(LorettaDiagnosticIds.TryMap(lorettaId, out _));
    }

    // DiagnosticId tops out at 9999. Loretta is nowhere near that today (its highest is 2000), but
    // it is a dependency, and growing past the ceiling must degrade rather than throw.
    [Fact]
    public void ANumberBeyondTheIdCeiling_MapsToNothing()
    {
        Assert.False(LorettaDiagnosticIds.TryMap("LUA99999", out _));
    }

    // Loretta owns the low numbers in the Syntax group; ours start above the reserved band, so a
    // future Loretta code can never collide with one we assigned by hand.
    [Fact]
    public void TheReservedBand_CoversEveryNumberLorettaCanProduce()
    {
        Assert.True(LorettaDiagnosticIds.TryMap("LUA2999", out var top));
        Assert.True(top.Number < LorettaDiagnosticIds.FirstOwnSyntaxNumber);
    }
}
