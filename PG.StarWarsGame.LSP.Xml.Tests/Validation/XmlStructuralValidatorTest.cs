// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

public sealed class XmlStructuralValidatorTest
{
    private static readonly XmlStructuralValidator Sut = new();

    [Fact]
    public void Well_formed_xml_returns_no_errors()
    {
        const string xml = "<Root><Child attr=\"val\">text</Child></Root>";
        Assert.Empty(Sut.Validate(xml));
    }

    [Fact]
    public void Mismatched_closing_tag_returns_error_mentioning_the_tag()
    {
        const string xml = "<Foo><Bar></Foo>";
        var errors = Sut.Validate(xml);
        var e = Assert.Single(errors);
        Assert.Contains("Bar", e.Reason);
    }

    [Fact]
    public void Unclosed_tag_returns_error()
    {
        const string xml = "<Foo><Bar>";
        Assert.NotEmpty(Sut.Validate(xml));
    }

    [Fact]
    public void Malformed_attribute_unquoted_value_returns_error()
    {
        const string xml = "<Foo attr=value />";
        Assert.NotEmpty(Sut.Validate(xml));
    }

    [Fact]
    public void Error_carries_nonnegative_line_and_column()
    {
        const string xml = "<Foo>\n  <Bar>\n</Foo>";
        var errors = Sut.Validate(xml);
        var e = Assert.Single(errors);
        Assert.True(e.Line >= 0);
        Assert.True(e.Column >= 0);
    }

    [Fact]
    public void Multiline_mismatch_error_is_on_correct_line()
    {
        // Mismatch is on line 3 (0-based: 2)
        const string xml = "<Foo>\n  <Bar>\n</Foo>";
        var errors = Sut.Validate(xml);
        var e = Assert.Single(errors);
        Assert.Equal(2, e.Line);
    }
}