// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Util;

public sealed class LineOffsetIndexTest
{
    // ── Basic positions ──────────────────────────────────────────────────────

    [Fact]
    public void GetPosition_OffsetZero_ReturnsOrigin()
    {
        var index = new LineOffsetIndex("hello");

        Assert.Equal((0, 0), index.GetPosition(0));
    }

    [Fact]
    public void GetPosition_MidFirstLine_ReturnsColumnOnLineZero()
    {
        var index = new LineOffsetIndex("hello world");

        Assert.Equal((0, 6), index.GetPosition(6));
    }

    [Fact]
    public void GetPosition_StartOfSecondLine_ReturnsLineOneColumnZero()
    {
        var index = new LineOffsetIndex("ab\ncd");

        Assert.Equal((1, 0), index.GetPosition(3));
    }

    [Fact]
    public void GetPosition_MidSecondLine_ReturnsLineOneColumn()
    {
        var index = new LineOffsetIndex("ab\ncd");

        Assert.Equal((1, 1), index.GetPosition(4));
    }

    [Fact]
    public void GetPosition_OffsetAtNewlineChar_StaysOnCurrentLine()
    {
        // The '\n' at offset 2 belongs to line 0 — only characters BEFORE the
        // offset are counted, matching XmlUtility.OffsetToPosition.
        var index = new LineOffsetIndex("ab\ncd");

        Assert.Equal((0, 2), index.GetPosition(2));
    }

    // ── CRLF: '\r' counts as a column character, same as OffsetToPosition ────

    [Fact]
    public void GetPosition_CrLf_CarriageReturnCountsAsColumn()
    {
        var index = new LineOffsetIndex("ab\r\ncd");

        Assert.Equal((0, 2), index.GetPosition(2)); // at '\r'
        Assert.Equal((0, 3), index.GetPosition(3)); // at '\n'
        Assert.Equal((1, 0), index.GetPosition(4)); // at 'c'
        Assert.Equal((1, 1), index.GetPosition(5)); // at 'd'
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void GetPosition_EmptyText_ReturnsOrigin()
    {
        var index = new LineOffsetIndex(string.Empty);

        Assert.Equal((0, 0), index.GetPosition(0));
        Assert.Equal((0, 5), index.GetPosition(5));
    }

    [Fact]
    public void GetPosition_NegativeOffset_ClampsColumnToZero()
    {
        // HtmlAgilityPack yields -1 stream positions for synthetic nodes; the
        // column must clamp to 0 exactly like XmlUtility.OffsetToPosition.
        var index = new LineOffsetIndex("ab\ncd");

        Assert.Equal((0, 0), index.GetPosition(-1));
    }

    [Fact]
    public void GetPosition_OffsetBeyondLength_CountsFromLastLineStart()
    {
        // Offsets past the end resolve against the last line, with the column
        // running past the text — mirrors OffsetToPosition's unclamped column.
        var index = new LineOffsetIndex("ab\ncd");

        Assert.Equal((1, 4), index.GetPosition(7));
    }

    [Fact]
    public void GetPosition_TrailingNewline_OffsetAtLengthIsOnFinalEmptyLine()
    {
        var index = new LineOffsetIndex("ab\n");

        Assert.Equal((1, 0), index.GetPosition(3));
    }

    [Fact]
    public void GetPosition_ConsecutiveNewlines_EmptyLinesResolve()
    {
        var index = new LineOffsetIndex("a\n\n\nb");

        Assert.Equal((1, 0), index.GetPosition(2));
        Assert.Equal((2, 0), index.GetPosition(3));
        Assert.Equal((3, 0), index.GetPosition(4));
    }

    // ── Equivalence with XmlUtility.OffsetToPosition ─────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("single line")]
    [InlineData("ab\ncd\nef")]
    [InlineData("ab\r\ncd\r\nef")]
    [InlineData("\n\n\n")]
    [InlineData("trailing newline\n")]
    [InlineData("<Unit Name=\"X\">\n  <Ref>A, B</Ref>\r\n</Unit>\n")]
    public void GetPosition_EveryOffset_MatchesOffsetToPosition(string text)
    {
        var index = new LineOffsetIndex(text);

        for (var offset = -2; offset <= text.Length + 3; offset++)
            Assert.Equal(XmlUtility.OffsetToPosition(text, offset), index.GetPosition(offset));
    }
}
