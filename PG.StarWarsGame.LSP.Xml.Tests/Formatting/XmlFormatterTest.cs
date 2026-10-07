// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Xml.Formatting;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Formatting;

/// <summary>Whitespace between markup is re-indented; nothing the document reads changes.</summary>
public sealed class XmlFormatterTest
{
    private const string Decl = "<?xml version=\"1.0\"?>\n";

    private static XmlFormatResult Run(string text, string indent = "\t", (int, int)? range = null)
    {
        return XmlFormatter.Format(ParsedXmlDocument.Parse(text), indent, range);
    }

    private static string Formatted(string text, string indent = "\t")
    {
        var result = Run(text, indent);
        Assert.Null(result.Refusal);
        return XmlStructureRepairs.Apply(text, result.Edits);
    }

    [Fact]
    public void NestedElements_AreIndentedByDepth()
    {
        const string input = Decl + "<Root>\n<A>\n    <B>1</B>\n</A>\n</Root>";
        Assert.Equal(Decl + "<Root>\n\t<A>\n\t\t<B>1</B>\n\t</A>\n</Root>", Formatted(input));
    }

    [Fact]
    public void IndentUnit_IsTheCallersChoice()
    {
        const string input = Decl + "<Root>\n<A>1</A>\n</Root>";
        Assert.Equal(Decl + "<Root>\n  <A>1</A>\n</Root>", Formatted(input, "  "));
    }

    [Fact]
    public void FormattedDocument_FormatsToNothing()
    {
        var once = Formatted(Decl + "<Root>\n  <!-- c -->\n<A>\n<B>1</B>\n\n\n</A>\n</Root>\n");
        Assert.Empty(Run(once).Edits);
    }

    [Fact]
    public void LineEndings_ArePreservedPerLine()
    {
        const string input = "<?xml version=\"1.0\"?>\r\n<Root>\r\n<A>1</A>\n<B>2</B>\r\n</Root>";
        Assert.Equal("<?xml version=\"1.0\"?>\r\n<Root>\r\n\t<A>1</A>\n\t<B>2</B>\r\n</Root>", Formatted(input));
    }

    [Fact]
    public void TrailingWhitespace_IsRemoved_AndBlankLinesCollapseToOne()
    {
        const string input = Decl + "<Root>  \t\n<A>1</A>\t\n\n  \n\n<B>2</B>\n\n<C>3</C>\n</Root>";
        Assert.Equal(Decl + "<Root>\n\t<A>1</A>\n\n\t<B>2</B>\n\n\t<C>3</C>\n</Root>", Formatted(input));
    }

    [Fact]
    public void LeafValues_AreNeverTouched()
    {
        const string input = Decl + "<Root>\n\t<A>\n   x  \n</A>\n\t<B>\n</B>\n</Root>";
        Assert.Equal(input, Formatted(input));
    }

    [Fact]
    public void ElementWithText_IsNeverTouched_EvenBetweenItsComments()
    {
        // The game joins the text around a comment, so whitespace there is part of the value.
        const string input = Decl + "<Root>\n\t<A>x<!-- c -->\n      <!-- d -->y</A>\n</Root>";
        Assert.Equal(input, Formatted(input));
    }

    [Fact]
    public void Comments_AreIndentedLikeElements_TheirTextKept()
    {
        const string input = Decl + "<Root>\n<!--  keep   this  -->\n<A>1</A>\n</Root>";
        Assert.Equal(Decl + "<Root>\n\t<!--  keep   this  -->\n\t<A>1</A>\n</Root>", Formatted(input));
    }

    [Fact]
    public void SameLineLayout_AttributesAndEntities_AreKept()
    {
        const string input = Decl + "<Root>\n<A  b='x'   a=\"&amp;\"><B>1</B><C/></A>\n</Root>";
        Assert.Equal(Decl + "<Root>\n\t<A  b='x'   a=\"&amp;\"><B>1</B><C/></A>\n</Root>", Formatted(input));
    }

    [Fact]
    public void MalformedDocument_IsRefused_WithItsLine()
    {
        var result = Run(Decl + "<Root>\n<A>1</B>\n</Root>");

        Assert.Empty(result.Edits);
        Assert.NotNull(result.Refusal);
        Assert.Contains("line 3", result.Refusal);
    }

    [Fact]
    public void Range_OnlyEditsInsideIt()
    {
        const string input = Decl + "<Root>\n<A>1</A>\n<B>2</B>\n<C>3</C>\n</Root>";
        var start = input.IndexOf("<B>", StringComparison.Ordinal);
        var end = input.IndexOf("</B>", StringComparison.Ordinal);

        var result = Run(input, range: (input.LastIndexOf('\n', start) + 1, end));

        var edit = Assert.Single(result.Edits);
        Assert.Equal(input.LastIndexOf('\n', start) + 1, edit.Start);
        Assert.Equal("\t", edit.NewText);
    }
}
