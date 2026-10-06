// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Util;

/// <summary>
///     Exact source spans of every element: start tag, its name, end tag and its name, by offset.
///     Pairing is the lenient tree's; positions come from the text, never from per-node line and
///     column, which drift for nested elements.
/// </summary>
public sealed class XmlElementSpansTest
{
    private static XmlElementSpans Build(string text) =>
        XmlElementSpans.Build(XmlUtility.CreateHtmlDocument(text), text);

    private static string Slice(string text, (int Start, int Length) span) => text.Substring(span.Start, span.Length);

    [Fact]
    public void NestedElements_NamesAndTagsAreExact()
    {
        const string text = "<?xml version=\"1.0\"?>\n<Root>\n\t<Unit Name=\"x\">\n\t\t<Max_Speed>1</Max_Speed>\n\t</Unit>\n</Root>";
        var spans = Build(text);

        var unit = Assert.Single(spans.Elements, e => e.Name == "Unit");
        Assert.Equal("Unit", Slice(text, unit.StartName));
        Assert.Equal("<Unit Name=\"x\">", Slice(text, unit.StartTag));
        Assert.Equal("Unit", Slice(text, unit.EndName!.Value));
        Assert.Equal("</Unit>", Slice(text, unit.EndTag!.Value));

        var speed = Assert.Single(spans.Elements, e => e.Name == "Max_Speed");
        Assert.Equal("Max_Speed", Slice(text, speed.EndName!.Value));
        Assert.Same(unit, speed.Parent);
    }

    [Fact]
    public void NamesKeepTheirCase()
    {
        const string text = "<Root><SoundFX_Name>x</SoundFX_Name></Root>";
        var e = Assert.Single(Build(text).Elements, e => e.Name == "SoundFX_Name");
        Assert.Equal("SoundFX_Name", Slice(text, e.EndName!.Value));
    }

    [Fact]
    public void SelfClosingElement_HasNoEndTag()
    {
        const string text = "<Root><A Name=\"x\"/></Root>";
        var a = Assert.Single(Build(text).Elements, e => e.Name == "A");
        Assert.Equal("<A Name=\"x\"/>", Slice(text, a.StartTag));
        Assert.Null(a.EndTag);
    }

    [Fact]
    public void AttributeValueHoldingAngleBracket_DoesNotEndTheStartTag()
    {
        const string text = "<Root><A Note=\"a > b\">1</A></Root>";
        var a = Assert.Single(Build(text).Elements, e => e.Name == "A");
        Assert.Equal("<A Note=\"a > b\">", Slice(text, a.StartTag));
    }

    [Fact]
    public void MissingCloseTag_ElementHasNoEndTag_OthersStillPair()
    {
        const string text = "<Root>\n\t<A>\n\t<B>1</B>\n</Root>";
        var spans = Build(text);

        Assert.Null(Assert.Single(spans.Elements, e => e.Name == "A").EndTag);
        Assert.Equal("</B>", Slice(text, Assert.Single(spans.Elements, e => e.Name == "B").EndTag!.Value));
        Assert.Equal("</Root>", Slice(text, Assert.Single(spans.Elements, e => e.Name == "Root").EndTag!.Value));
    }

    [Fact]
    public void EndTagNamingAnotherElement_IsNotPaired()
    {
        const string text = "<Root>\n\t<A>1</B>\n</Root>";
        Assert.Null(Assert.Single(Build(text).Elements, e => e.Name == "A").EndTag);
    }

    [Fact]
    public void Comments_AreListedWithTheirSpans()
    {
        const string text = "<Root>\n\t<!-- one\n\t two -->\n\t<A>1</A>\n</Root>";
        var comment = Assert.Single(Build(text).Comments);
        Assert.Equal("<!-- one\n\t two -->", Slice(text, comment));
    }

    [Fact]
    public void ElementAt_FindsTheInnermostElementContainingAnOffset()
    {
        const string text = "<Root>\n\t<A>12</A>\n</Root>";
        var spans = Build(text);
        Assert.Equal("A", spans.ElementAt(text.IndexOf("12", StringComparison.Ordinal))!.Name);
        Assert.Equal("Root", spans.ElementAt(text.IndexOf("\n\t<A>", StringComparison.Ordinal))!.Name);
        Assert.Null(spans.ElementAt(text.Length));
    }
}
