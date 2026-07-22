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

    // ── ToPascalCase ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("lucky_shot_attack_ability", "LuckyShotAttackAbility")]
    [InlineData("combat_bonus_ability", "CombatBonusAbility")]
    [InlineData("force_cloak_ability", "ForceCloakAbility")]
    [InlineData("Lucky_Shot_Attack_Ability", "LuckyShotAttackAbility")]
    [InlineData("single", "Single")]
    [InlineData("", "")]
    public void ToPascalCase_Conversions(string input, string expected)
    {
        Assert.Equal(expected, XmlUtility.ToPascalCase(input));
    }

    // ── ToSnakeCase ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("LuckyShotAttackAbility", "Lucky_Shot_Attack_Ability")]
    [InlineData("ForceCloakAbility", "Force_Cloak_Ability")]
    [InlineData("UnitAbility", "Unit_Ability")]
    [InlineData("SpaceUnit", "Space_Unit")]
    [InlineData("Single", "Single")]
    [InlineData("", "")]
    public void ToSnakeCase_Conversions(string input, string expected)
    {
        Assert.Equal(expected, XmlUtility.ToSnakeCase(input));
    }

    [Theory]
    [InlineData("LuckyShotAttackAbility")]
    [InlineData("ForceCloakAbility")]
    [InlineData("UnitAbility")]
    public void ToSnakeCase_RoundTrip_WithToPascalCase(string pascalInput)
    {
        Assert.Equal(pascalInput, XmlUtility.ToPascalCase(XmlUtility.ToSnakeCase(pascalInput)));
    }

    // ── IsOnTagName ───────────────────────────────────────────────────────────

    [Fact]
    public void IsOnTagName_CursorOnOpeningTagName_ReturnsTrue()
    {
        // Line 1: <Foo/>  - 'F' at col 1, 'o' at 2, 'o' at 3
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
        // Line 1: <Foo>bar</Foo> - 'b' of "bar" at col 5
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
        // Line 1: <Max_Speed>500</Max_Speed> - '<' at 14, '/' at 15
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

    // ── GetValuePosition (highlight the value, not the tag) ───────────────────

    [Fact]
    public void GetValuePosition_SingleLineWithSurroundingWhitespace_AnchorsOnTrimmedValue()
    {
        const string text = "<Root>\n<Foo>  bar  </Foo>\n</Root>";
        var doc = XmlUtility.CreateHtmlDocument(text);
        XmlUtility.TryFindNode(doc, 1, out var node);

        var (line, col, len) = XmlUtility.GetValuePosition(node!, new LineOffsetIndex(text));

        Assert.Equal(1, line);
        Assert.Equal(text.Split('\n')[1].IndexOf("bar", StringComparison.Ordinal), col); // past '<Foo>' and spaces
        Assert.Equal(3, len);
    }

    [Fact]
    public void GetValuePosition_MultilineElement_AnchorsOnValueLine()
    {
        const string text = "<Root>\n<Foo>\nbar\n</Foo>\n</Root>";
        var doc = XmlUtility.CreateHtmlDocument(text);
        XmlUtility.TryFindNode(doc, 1, out var node);

        var (line, col, len) = XmlUtility.GetValuePosition(node!, new LineOffsetIndex(text));

        Assert.Equal(2, line); // "bar" is on line 2, not the <Foo> tag line
        Assert.Equal(0, col);
        Assert.Equal(3, len);
    }

    [Fact]
    public void GetInnerOffsetValuePosition_ListItemAtOffset_AnchorsOnThatItem()
    {
        // <Foo>aa, bb</Foo>: the second item "bb" is at inner offset 4 (past "aa, ").
        const string text = "<Root>\n<Foo>aa, bb</Foo>\n</Root>";
        var doc = XmlUtility.CreateHtmlDocument(text);
        XmlUtility.TryFindNode(doc, 1, out var node);

        var (line, col, len) = XmlUtility.GetInnerOffsetValuePosition(node!, 4, 2, new LineOffsetIndex(text));

        Assert.Equal(1, line);
        Assert.Equal(text.Split('\n')[1].IndexOf("bb", StringComparison.Ordinal), col);
        Assert.Equal(2, len);
    }

    [Fact]
    public void GetValuePosition_MatchesInnerOffsetCompanion_ForWholeValue()
    {
        // The whole-value helper must agree with the offset primitive it delegates to.
        const string text = "<Root>\n<Foo>  bar  </Foo>\n</Root>";
        var doc = XmlUtility.CreateHtmlDocument(text);
        XmlUtility.TryFindNode(doc, 1, out var node);
        var lineIndex = new LineOffsetIndex(text);

        var whole = XmlUtility.GetValuePosition(node!, lineIndex);
        var viaOffset = XmlUtility.GetInnerOffsetValuePosition(node!, 2, 3, lineIndex); // "  " leading, "bar"

        Assert.Equal(viaOffset, whole);
    }

    // ── FindEnclosingElement ──────────────────────────────────────────────────

    [Fact]
    public void FindEnclosingElement_CursorInsideLeafElement_ReturnsLeaf()
    {
        // line 0: <Root>
        // line 1: <Child>hello</Child>   ← cursor line
        // line 2: </Root>
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Child>hello</Child>\n</Root>");
        var node = XmlUtility.FindEnclosingElement(doc, 1);
        Assert.NotNull(node);
        Assert.Equal("child", node!.Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindEnclosingElement_CursorBetweenSiblings_ReturnsParent()
    {
        // line 0: <Root>
        // line 1:   <A>1</A>
        // line 2:   ← cursor (between siblings, inside Root)
        // line 3:   <B>2</B>
        // line 4: </Root>
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n  <A>1</A>\n  \n  <B>2</B>\n</Root>");
        var node = XmlUtility.FindEnclosingElement(doc, 2);
        Assert.NotNull(node);
        Assert.Equal("root", node!.Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindEnclosingElement_CursorInsideNestedElement_ReturnsDeepest()
    {
        // line 0: <Outer>
        // line 1:   <Inner>
        // line 2:     ← cursor
        // line 3:   </Inner>
        // line 4: </Outer>
        var doc = XmlUtility.CreateHtmlDocument("<Outer>\n  <Inner>\n    \n  </Inner>\n</Outer>");
        var node = XmlUtility.FindEnclosingElement(doc, 2);
        Assert.NotNull(node);
        Assert.Equal("inner", node!.Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindEnclosingElement_TruncatedDocument_ReturnsUnclosedParent()
    {
        // Simulates text truncated at cursor before a '<' typed inside GameObjectType.
        // The truncated text has no </GameObjectType> - HAP auto-closes it.
        const string truncated =
            "<GameObjectFiles>\n  <GameObjectType Name=\"Foo\">\n    <Max_Speed>500</Max_Speed>\n    ";
        var doc = XmlUtility.CreateHtmlDocument(truncated);
        // cursor line = 3 (the line with just spaces, inside GameObjectType)
        var node = XmlUtility.FindEnclosingElement(doc, 3);
        Assert.NotNull(node);
        Assert.Equal("gameobjecttype", node!.Name, StringComparer.OrdinalIgnoreCase);
    }


    [Fact]
    public void FindEnclosingElement_SelfClosingLeafAboveCursor_DoesNotStealEnclosing()
    {
        // Reproduces a real-file bug: self-closing elements (<Tag />) get EndNode = self (HAP
        // treats them as self-closing), which previously caused endLine = int.MaxValue. Because
        // such elements are deeper than real container ancestors, they wrongly win the depth
        // comparison for any cursor below them.
        //
        // line 0: <GameObjectFiles>
        // line 1:   <SpaceUnit Name="Unit1">
        // line 2:     <Tactical_Build_Prerequisites />     ← self-closing; used to steal result
        // line 3:     <Tactical_Production_Queue>X</...>
        // line 4:   </SpaceUnit>
        // line 5:   <SpaceUnit Name="Unit2">
        // line 6:     ← cursor (inside Unit2 but before any child starts)
        // line 7:     <Max_Speed>100.0</Max_Speed>
        // line 8:   </SpaceUnit>
        // line 9: </GameObjectFiles>
        const string xml =
            "<GameObjectFiles>\n" +
            "  <SpaceUnit Name=\"Unit1\">\n" +
            "    <Tactical_Build_Prerequisites />\n" +
            "    <Tactical_Production_Queue>X</Tactical_Production_Queue>\n" +
            "  </SpaceUnit>\n" +
            "  <SpaceUnit Name=\"Unit2\">\n" +
            "    \n" +
            "    <Max_Speed>100.0</Max_Speed>\n" +
            "  </SpaceUnit>\n" +
            "</GameObjectFiles>";

        var doc = XmlUtility.CreateHtmlDocument(xml);
        var node = XmlUtility.FindEnclosingElement(doc, 6); // cursor inside Unit2

        Assert.NotNull(node);
        Assert.Equal("spaceunit", node!.Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindEnclosingElement_LeafWithSyntheticAutoClosedEndNode_DoesNotStealEnclosingFromSiblingContainer()
    {
        // HAP auto-closes block elements like <p> when the next block element starts, producing a
        // synthetic EndNode with Line=0.  Both <p> leaves land at depth=1 with endLine=int.MaxValue.
        // <div> is also at depth=1 (a true sibling because it is a block element that HAP places
        // at the same level).  The first <p> is visited first in document order: with endLine=
        // int.MaxValue it "contains" the cursor, wins the depth=1 slot, and prevents <div> from
        // ever being considered (same depth, strict > comparison).
        //
        // line 0: <Root>
        // line 1:   <p>leaf0  ← synthetic EndNode(Line=0) → int.MaxValue → steals cursor
        // line 2:   <p>leaf1  ← same
        // line 3:   <div>     ← depth=1 sibling; loses because depth not > 1
        // line 4:     (cursor - should return div, not p)
        // line 5:   </div>
        // line 6: </Root>
        const string xml =
            "<Root>\n" +
            "  <p>leaf0\n" +
            "  <p>leaf1\n" +
            "  <div>\n" +
            "    \n" +
            "  </div>\n" +
            "</Root>";
        var doc = XmlUtility.CreateHtmlDocument(xml);
        var node = XmlUtility.FindEnclosingElement(doc, 4); // cursor inside <div>
        Assert.NotNull(node);
        Assert.Equal("div", node!.Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindEnclosingElement_CursorAfterAllElements_ReturnsNull()
    {
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n<Child>x</Child>\n</Root>");
        // Line 5 is beyond the document - nothing contains it
        var node = XmlUtility.FindEnclosingElement(doc, 5);
        Assert.Null(node);
    }

    // ── GetTagBracketColumn ───────────────────────────────────────────────────

    [Fact]
    public void GetTagBracketColumn_TagAtStartOfLine_ReturnsZero()
    {
        // "<Foo>bar</Foo>" - '<' is at column 0
        var doc = XmlUtility.CreateHtmlDocument("<Foo>bar</Foo>");
        XmlUtility.TryFindNode(doc, 0, out var node);
        Assert.Equal(0, XmlUtility.GetTagBracketColumn(node));
    }

    [Fact]
    public void GetTagBracketColumn_IndentedTag_ReturnsIndentColumn()
    {
        // line 1: "  <Bar>x</Bar>" - '<' is at column 2
        var doc = XmlUtility.CreateHtmlDocument("<Root>\n  <Bar>x</Bar>\n</Root>");
        XmlUtility.TryFindNode(doc, 1, out var node);
        Assert.Equal(2, XmlUtility.GetTagBracketColumn(node));
    }

    [Fact]
    public void GetTagBracketColumn_NullNode_ReturnsInvalidMarker()
    {
        Assert.Equal(XmlUtility.InvalidLineMarker, XmlUtility.GetTagBracketColumn(null));
    }

    // ── GetOpeningTagLength ───────────────────────────────────────────────────

    [Fact]
    public void GetOpeningTagLength_SimpleTag_ReturnsCorrectLength()
    {
        // "<Foo>bar</Foo>" - opening tag "<Foo>" has length 5
        var doc = XmlUtility.CreateHtmlDocument("<Foo>bar</Foo>");
        XmlUtility.TryFindNode(doc, 0, out var node);
        Assert.Equal(5, XmlUtility.GetOpeningTagLength(node));
    }

    [Fact]
    public void GetOpeningTagLength_TagWithAttribute_IncludesAttribute()
    {
        // "<Unit Name=\"Foo\">x</Unit>" - opening tag has length 17
        var doc = XmlUtility.CreateHtmlDocument("<Unit Name=\"Foo\">x</Unit>");
        XmlUtility.TryFindNode(doc, 0, out var node);
        Assert.Equal(17, XmlUtility.GetOpeningTagLength(node));
    }

    [Fact]
    public void GetOpeningTagLength_NullNode_ReturnsZero()
    {
        Assert.Equal(0, XmlUtility.GetOpeningTagLength(null));
    }

    // ── AdvancePosition ──────────────────────────────────────────────────────

    [Fact]
    public void AdvancePosition_SameLine_AddsToColumn()
    {
        var (line, col) = XmlUtility.AdvancePosition(3, 10, "TEXT_A TEXT_B", 7);
        Assert.Equal(3, line);
        Assert.Equal(17, col);
    }

    [Fact]
    public void AdvancePosition_CrossesNewline_ResetsColumnAndAdvancesLine()
    {
        // "TEXT_A\nTEXT_B" - offset 7 is the 'T' right after the newline.
        var (line, col) = XmlUtility.AdvancePosition(3, 10, "TEXT_A\nTEXT_B", 7);
        Assert.Equal(4, line);
        Assert.Equal(0, col);
    }

    [Fact]
    public void AdvancePosition_ZeroOffset_ReturnsStartPosition()
    {
        var (line, col) = XmlUtility.AdvancePosition(3, 10, "TEXT_A TEXT_B", 0);
        Assert.Equal(3, line);
        Assert.Equal(10, col);
    }

    [Fact]
    public void AdvancePosition_MultipleNewlines_AdvancesLineForEach()
    {
        // "A\nB\nC" - offset 4 is 'C', two newlines crossed.
        var (line, col) = XmlUtility.AdvancePosition(0, 0, "A\nB\nC", 4);
        Assert.Equal(2, line);
        Assert.Equal(0, col);
    }

    // ── GetElementEndPosition ─────────────────────────────────────────────────

    [Fact]
    public void GetElementEndPosition_SingleLineElement_ReturnsPositionAfterClosingTag()
    {
        // "<Root>\n<Foo>bar</Foo>\n</Root>" - line 1 is "<Foo>bar</Foo>", closing '>' at col 14.
        const string text = "<Root>\n<Foo>bar</Foo>\n</Root>";
        var doc = XmlUtility.CreateHtmlDocument(text);
        XmlUtility.TryFindNode(doc, 1, out var node);

        var (line, col) = XmlUtility.GetElementEndPosition(node!, text);

        Assert.Equal(1, line);
        Assert.Equal(14, col);
    }

    [Fact]
    public void GetElementEndPosition_MultilineElement_ReturnsPositionOnClosingTagLine()
    {
        // Line 0: <Root>  Line 1: <Foo>  Line 2: bar  Line 3: </Foo>  Line 4: </Root>
        // Line 3 "</Foo>" - closing '>' at index 5, so end position is col 6.
        const string text = "<Root>\n<Foo>\nbar\n</Foo>\n</Root>";
        var doc = XmlUtility.CreateHtmlDocument(text);
        XmlUtility.TryFindNode(doc, 1, out var node);

        var (line, col) = XmlUtility.GetElementEndPosition(node!, text);

        Assert.Equal(3, line);
        Assert.Equal(6, col);
    }

    [Fact]
    public void GetElementEndPosition_SelfClosingElement_ReturnsPositionAfterOpeningTag()
    {
        // "<Root>\n<Foo/>\n</Root>" - line 1 "<Foo/>", closing '>' at index 5, so end position is col 6.
        const string text = "<Root>\n<Foo/>\n</Root>";
        var doc = XmlUtility.CreateHtmlDocument(text);
        XmlUtility.TryFindNode(doc, 1, out var node);

        var (line, col) = XmlUtility.GetElementEndPosition(node!, text);

        Assert.Equal(1, line);
        Assert.Equal(6, col);
    }

    [Fact]
    public void GetElementEndPosition_ElementWithAttributes_IgnoresAttributeQuotedAngleBrackets()
    {
        // Attribute values can't legally contain unescaped '<'/'>' in well-formed XML, but confirm
        // the search starts from the closing-tag node, not from the element's own start.
        const string text = "<Root>\n<Unit Name=\"Foo\">x</Unit>\n</Root>";
        var doc = XmlUtility.CreateHtmlDocument(text);
        XmlUtility.TryFindNode(doc, 1, out var node);

        var (line, col) = XmlUtility.GetElementEndPosition(node!, text);

        Assert.Equal(1, line);
        Assert.Equal("<Unit Name=\"Foo\">x</Unit>".Length, col);
    }

    // ── OffsetToPosition ──────────────────────────────────────────────────────

    [Fact]
    public void OffsetToPosition_OffsetOnFirstLine_ReturnsLineZero()
    {
        var (line, col) = XmlUtility.OffsetToPosition("hello world", 6);
        Assert.Equal(0, line);
        Assert.Equal(6, col);
    }

    [Fact]
    public void OffsetToPosition_OffsetAfterNewline_ReturnsLine1()
    {
        // "ab\ncd" - offset 3 = 'c', line 1 col 0
        var (line, col) = XmlUtility.OffsetToPosition("ab\ncd", 3);
        Assert.Equal(1, line);
        Assert.Equal(0, col);
    }

    [Fact]
    public void OffsetToPosition_OffsetMidSecondLine_ReturnsCorrectPosition()
    {
        // "ab\ncd\nef" - offset 7 = 'e' on line 2 at col 0... wait let's count:
        // 0:'a' 1:'b' 2:'\n' 3:'c' 4:'d' 5:'\n' 6:'e' 7:'f'
        // offset 6 = 'e' → line 2, col 0
        var (line, col) = XmlUtility.OffsetToPosition("ab\ncd\nef", 6);
        Assert.Equal(2, line);
        Assert.Equal(0, col);
    }

    [Fact]
    public void OffsetToPosition_ZeroOffset_ReturnsOrigin()
    {
        var (line, col) = XmlUtility.OffsetToPosition("anything", 0);
        Assert.Equal(0, line);
        Assert.Equal(0, col);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void OffsetToPosition_NegativeOffset_ClampsColumnToZero(int offset)
    {
        // HtmlAgilityPack can report InnerStartIndex/-1; a negative column would crash the LSP client.
        var (line, col) = XmlUtility.OffsetToPosition("anything", offset);
        Assert.Equal(0, line);
        Assert.Equal(0, col);
    }

    // ── PositionToOffset ──────────────────────────────────────────────────────

    [Fact]
    public void PositionToOffset_LineZero_ReturnsCharacter()
    {
        var offset = XmlUtility.PositionToOffset("hello world", 0, 6);
        Assert.Equal(6, offset);
    }

    [Fact]
    public void PositionToOffset_SecondLine_SkipsFirstLineAndNewline()
    {
        // "ab\ncd" - line 1, char 1 = 'd' → offset 4
        var offset = XmlUtility.PositionToOffset("ab\ncd", 1, 1);
        Assert.Equal(4, offset);
    }

    [Fact]
    public void PositionToOffset_ThirdLine_SkipsTwoNewlines()
    {
        // "ab\ncd\nef" - line 2, char 0 = 'e' → offset 6
        var offset = XmlUtility.PositionToOffset("ab\ncd\nef", 2, 0);
        Assert.Equal(6, offset);
    }

    [Fact]
    public void PositionToOffset_RoundTripsWithOffsetToPosition()
    {
        const string text = "abc\ndefgh\nij";
        const int offset = 8;
        var (line, col) = XmlUtility.OffsetToPosition(text, offset);
        Assert.Equal(offset, XmlUtility.PositionToOffset(text, line, col));
    }

    [Fact]
    public void PositionToOffset_LineBeyondText_ClampsToTextLength()
    {
        var offset = XmlUtility.PositionToOffset("ab\ncd", 5, 0);
        Assert.Equal(5, offset);
    }
}