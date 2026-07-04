// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Util;

public sealed class ParsedXmlDocumentTest
{
    [Fact]
    public void Parse_ExposesOriginalText()
    {
        const string text = "<Root><Unit Name=\"X\"/></Root>";

        var parsed = ParsedXmlDocument.Parse(text);

        Assert.Same(text, parsed.Text);
    }

    [Fact]
    public void Parse_HtmlIsParsedWithXmlUtilitySemantics()
    {
        var parsed = ParsedXmlDocument.Parse("<Root>\n  <Unit Name=\"X\"/>\n</Root>");

        var unit = parsed.Html.DocumentNode.Descendants()
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element && n.Name == "unit");
        Assert.NotNull(unit);
        Assert.Equal(2, unit!.Line);
    }

    [Fact]
    public void LineIndex_ResolvesOffsetsAgainstText()
    {
        var parsed = ParsedXmlDocument.Parse("ab\ncd");

        Assert.Equal((1, 1), parsed.LineIndex.GetPosition(4));
    }

    [Fact]
    public void Lines_SplitsOnNewlines()
    {
        var parsed = ParsedXmlDocument.Parse("a\nb\nc");

        Assert.Equal(["a", "b", "c"], parsed.Lines);
    }

    [Fact]
    public void Lines_ReturnsSameArrayOnRepeatedAccess()
    {
        var parsed = ParsedXmlDocument.Parse("a\nb");

        Assert.Same(parsed.Lines, parsed.Lines);
    }
}
