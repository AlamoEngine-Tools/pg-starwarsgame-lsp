// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Workspace;

public sealed class PreOpenBufferTest
{
    [Fact]
    public void RecordOpen_AddsToPending_ReturnedByDrain()
    {
        var buf = new PreOpenBuffer();
        buf.RecordOpen("file:///a.xml", "<A/>", 1);

        var result = buf.DrainAndClose();

        Assert.Single(result);
        Assert.Equal("file:///a.xml", result[0].Uri);
        Assert.Equal("<A/>", result[0].Text);
        Assert.Equal(1, result[0].Version);
    }

    [Fact]
    public void DrainAndClose_ClearsBuffer()
    {
        var buf = new PreOpenBuffer();
        buf.RecordOpen("file:///a.xml", "<A/>", 1);

        _ = buf.DrainAndClose();
        var second = buf.DrainAndClose();

        Assert.Empty(second);
    }

    [Fact]
    public void RecordOpen_AfterClose_IsNoOp()
    {
        var buf = new PreOpenBuffer();
        _ = buf.DrainAndClose();

        buf.RecordOpen("file:///late.xml", "<Late/>", 2);
        var result = buf.DrainAndClose();

        Assert.Empty(result);
    }

    [Fact]
    public void DrainAndClose_CalledTwice_ReturnsEmptyOnSecondCall()
    {
        var buf = new PreOpenBuffer();
        buf.RecordOpen("file:///a.xml", "<A/>", 1);
        buf.RecordOpen("file:///b.xml", "<B/>", 1);

        var first = buf.DrainAndClose();
        var second = buf.DrainAndClose();

        Assert.Equal(2, first.Count);
        Assert.Empty(second);
    }
}