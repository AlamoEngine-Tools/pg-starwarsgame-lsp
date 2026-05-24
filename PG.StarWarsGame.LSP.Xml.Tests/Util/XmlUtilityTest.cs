// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Util;

public sealed class XmlUtilityTest
{
    // ── SplitListWithOffsets ─────────────────────────────────────────────────

    [Fact]
    public void SplitListWithOffsets_TwoSpaceSeparated_ReturnsCorrectOffsets()
    {
        var result = XmlUtility.SplitListWithOffsets("X_Wing Y_Wing");

        Assert.Equal(2, result.Count);
        Assert.Equal(("X_Wing", 0), result[0]);
        Assert.Equal(("Y_Wing", 7), result[1]);
    }

    [Fact]
    public void SplitListWithOffsets_CommaSeparated_ReturnsCorrectOffsets()
    {
        var result = XmlUtility.SplitListWithOffsets("X_Wing,Y_Wing");

        Assert.Equal(2, result.Count);
        Assert.Equal(("X_Wing", 0), result[0]);
        Assert.Equal(("Y_Wing", 7), result[1]);
    }

    [Fact]
    public void SplitListWithOffsets_Empty_ReturnsEmpty()
    {
        var result = XmlUtility.SplitListWithOffsets("");

        Assert.Empty(result);
    }

    [Fact]
    public void SplitListWithOffsets_Whitespace_ReturnsEmpty()
    {
        var result = XmlUtility.SplitListWithOffsets("   ");

        Assert.Empty(result);
    }

    [Fact]
    public void SplitListWithOffsets_SingleToken_ReturnsSingleEntryAtZero()
    {
        var result = XmlUtility.SplitListWithOffsets("X_Wing");

        var single = Assert.Single(result);
        Assert.Equal(("X_Wing", 0), single);
    }

    [Fact]
    public void SplitListWithOffsets_ThreeSpaceSeparated_ReturnsAllOffsets()
    {
        var result = XmlUtility.SplitListWithOffsets("A B C");

        Assert.Equal(3, result.Count);
        Assert.Equal(("A", 0), result[0]);
        Assert.Equal(("B", 2), result[1]);
        Assert.Equal(("C", 4), result[2]);
    }
}