// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     The game's reader decides first: a file it drops gets that one error and nothing from the
///     strict pass. A file it reads is then checked against standard XML, and whatever the game
///     tolerated is reported beside it.
/// </summary>
public sealed class XmlStructuralValidatorTest
{
    private const string Decl = "<?xml version=\"1.0\"?>\n";
    private static readonly XmlStructuralValidator Sut = new();

    [Fact]
    public void WellFormedXml_ReturnsNothing()
    {
        Assert.Empty(Sut.Validate(Decl + "<Root><Child attr=\"val\">text</Child></Root>"));
    }

    [Fact]
    public void FileTheGameDrops_OneErrorOnly_StrictNotRepeated()
    {
        // Strict XML rejects this too; it must not be reported twice.
        var e = Assert.Single(Sut.Validate(Decl + "<Foo>\n  <Bar>1</Baz>\n</Foo>"));
        Assert.Equal(XmlStrictnessCategory.EndTagMismatch, e.Category);
        Assert.Equal(2, e.Line);
        Assert.Contains("Bar", e.Reason);
    }

    [Fact]
    public void UnclosedTag_IsAnEndOfFileError()
    {
        var e = Assert.Single(Sut.Validate(Decl + "<Foo><Bar>"));
        Assert.Equal(XmlStrictnessCategory.UnexpectedEndOfFile, e.Category);
    }

    [Fact]
    public void UnquotedAttribute_IsAnAttributeError()
    {
        var e = Assert.Single(Sut.Validate(Decl + "<Foo><A attr=value>1</A></Foo>"));
        Assert.Equal(XmlStrictnessCategory.AttributeSyntax, e.Category);
    }

    [Fact]
    public void DeclarationEndingInAngleBracketOnly_IsStrictOnly()
    {
        var e = Assert.Single(Sut.Validate("<?xml version=\"1.0\">\n<Root><A>1</A></Root>"));
        Assert.Equal(XmlStrictnessCategory.MalformedDeclaration, e.Category);
        Assert.Equal(0, e.Line);
    }

    [Fact]
    public void StrayAmpersand_IsStrictOnly()
    {
        var e = Assert.Single(Sut.Validate(Decl + "<Root>\n\t<A>Tom & Jerry</A>\n</Root>"));
        Assert.Equal(XmlStrictnessCategory.StrayAmpersand, e.Category);
        Assert.Equal(2, e.Line);
    }

    [Fact]
    public void TextOutsideTheRoot_IsStrictOnly()
    {
        var e = Assert.Single(Sut.Validate(Decl + "<Root><A>1</A></Root>\ntrailing"));
        Assert.Equal(XmlStrictnessCategory.StrictOnly, e.Category);
    }

    [Fact]
    public void GameTolerances_ArePassedThrough_BesideTheStrictResult()
    {
        var errors = Sut.Validate(Decl + "<Root>\n\t<A>1</A>``\n\t<B>x & y</B>\n</Root>");
        Assert.Contains(errors, e => e.Category == XmlStrictnessCategory.CharacterDataAfterChild);
        Assert.Contains(errors, e => e.Category == XmlStrictnessCategory.StrayAmpersand);
    }

    [Fact]
    public void Positions_AreZeroBased()
    {
        var e = Assert.Single(Sut.Validate(Decl + "<Foo>\n  <Bar>\n</Foo>"));
        Assert.True(e.Line >= 0);
        Assert.True(e.Column >= 0);
    }
}