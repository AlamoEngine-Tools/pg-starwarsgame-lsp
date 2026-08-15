// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

public class PngHeaderTest
{
    [Fact]
    public void TryRead_PngWeWrote_ReportsItsDimensions()
    {
        var png = PngWriter.Write(26, 13, new byte[26 * 13 * 4]);

        Assert.True(PngHeader.TryRead(png, out var width, out var height));
        Assert.Equal(26, width);
        Assert.Equal(13, height);
    }

    // Non-square matters: reading IHDR's two big-endian ints in the wrong order is the classic bug
    // here, and a square fixture cannot catch it.
    [Fact]
    public void TryRead_WidthAndHeightDiffer_DoesNotTransposeThem()
    {
        var png = PngWriter.Write(262, 20, new byte[262 * 20 * 4]);

        Assert.True(PngHeader.TryRead(png, out var width, out var height));
        Assert.Equal(262, width);
        Assert.Equal(20, height);
    }

    [Fact]
    public void TryRead_NotAPng_Fails()
    {
        Assert.False(PngHeader.TryRead([0x00, 0x01, 0x02, 0x03], out _, out _));
    }

    [Fact]
    public void TryRead_TruncatedBeforeTheHeader_Fails()
    {
        var png = PngWriter.Write(4, 4, new byte[4 * 4 * 4]);

        Assert.False(PngHeader.TryRead(png.AsSpan(0, 20).ToArray(), out _, out _));
    }

    // A zero dimension would sail through an unvalidated read and then divide by zero in the
    // client's scale maths, so it is rejected at the boundary rather than passed on.
    [Fact]
    public void TryRead_HeaderClaimsZeroWidth_Fails()
    {
        var png = PngWriter.Write(8, 8, new byte[8 * 8 * 4]);
        // Zero the IHDR width in place; the CRC is not checked, so this is a header-only edit.
        png[16] = 0;
        png[17] = 0;
        png[18] = 0;
        png[19] = 0;

        Assert.False(PngHeader.TryRead(png, out _, out _));
    }
}
