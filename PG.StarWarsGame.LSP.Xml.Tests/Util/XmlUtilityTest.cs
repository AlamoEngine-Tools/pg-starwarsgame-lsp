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

    // ── IsOnTagName ───────────────────────────────────────────────────────────

    [Fact]
    public void IsOnTagName_CursorOnOpeningTagName_ReturnsTrue()
    {
        // Line 1: <Foo/>  — 'F' at col 1, 'o' at 2, 'o' at 3
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Foo/>\n</Root>");
        XmlUtility.TryFindNode(doc, 1, out var node);
        Assert.True(XmlUtility.IsOnTagName(node!, 1, 1));
        Assert.True(XmlUtility.IsOnTagName(node!, 1, 3));
    }

    [Fact]
    public void IsOnTagName_CursorOnOpeningAngleBracket_ReturnsFalse()
    {
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Foo/>\n</Root>");
        XmlUtility.TryFindNode(doc, 1, out var node);
        Assert.False(XmlUtility.IsOnTagName(node!, 1, 0)); // '<'
    }

    [Fact]
    public void IsOnTagName_CursorAfterOpeningName_ReturnsFalse()
    {
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Foo/>\n</Root>");
        XmlUtility.TryFindNode(doc, 1, out var node);
        Assert.False(XmlUtility.IsOnTagName(node!, 1, 4)); // '/'
    }

    [Fact]
    public void IsOnTagName_CursorOnContent_ReturnsFalse()
    {
        // Line 1: <Foo>bar</Foo> — 'b' of "bar" at col 5
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Foo>bar</Foo>\n</Root>");
        XmlUtility.TryFindNode(doc, 1, out var node);
        Assert.False(XmlUtility.IsOnTagName(node!, 1, 5));
    }

    [Fact]
    public void IsOnTagName_CursorOnSameLineClosingTagName_ReturnsTrue()
    {
        // Line 1: <Max_Speed>500</Max_Speed>
        // </Max_Speed>: '<' at 14, '/' at 15, 'M' at 16, last char at 24
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Max_Speed>500</Max_Speed>\n</Root>");
        XmlUtility.TryFindNode(doc, 1, out var node);
        Assert.True(XmlUtility.IsOnTagName(node!, 1, 16));
        Assert.True(XmlUtility.IsOnTagName(node!, 1, 24));
    }

    [Fact]
    public void IsOnTagName_CursorOnClosingAngleOrSlash_ReturnsFalse()
    {
        // Line 1: <Max_Speed>500</Max_Speed> — '<' at 14, '/' at 15
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Max_Speed>500</Max_Speed>\n</Root>");
        XmlUtility.TryFindNode(doc, 1, out var node);
        Assert.False(XmlUtility.IsOnTagName(node!, 1, 14)); // '<'
        Assert.False(XmlUtility.IsOnTagName(node!, 1, 15)); // '/'
    }

    [Fact]
    public void IsOnTagName_MultilineElement_CursorOnClosingTagLine_ReturnsTrue()
    {
        // Line 1: <Foo>   Line 2: bar   Line 3: </Foo>
        // </Foo> on line 3: '<' at 0, '/' at 1, 'F' at 2
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Foo>\nbar\n</Foo>\n</Root>");
        XmlUtility.TryFindNode(doc, 1, out var node);
        Assert.True(XmlUtility.IsOnTagName(node!, 3, 2));
    }

    [Fact]
    public void IsOnTagName_MultilineElement_CursorOnContentLine_ReturnsFalse()
    {
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Foo>\nbar\n</Foo>\n</Root>");
        XmlUtility.TryFindNode(doc, 1, out var node);
        Assert.False(XmlUtility.IsOnTagName(node!, 2, 0));
    }

    // ── TryFindNodeByClosingLine ──────────────────────────────────────────────

    [Fact]
    public void TryFindNodeByClosingLine_MultilineElement_FindsCorrectElement()
    {
        // </Foo> is on line 3
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Foo>\nbar\n</Foo>\n</Root>");
        var found = XmlUtility.TryFindNodeByClosingLine(doc, 3, out var node);
        Assert.True(found);
        Assert.Equal("foo", node!.Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryFindNodeByClosingLine_ContentLine_ReturnsFalse()
    {
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Foo>\nbar\n</Foo>\n</Root>");
        Assert.False(XmlUtility.TryFindNodeByClosingLine(doc, 2, out _));
    }

    [Fact]
    public void TryFindNodeByClosingLine_SelfClosingTag_ReturnsFalse()
    {
        // Self-closing has no separate EndNode
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Foo/>\n</Root>");
        Assert.False(XmlUtility.TryFindNodeByClosingLine(doc, 1, out _));
    }
}