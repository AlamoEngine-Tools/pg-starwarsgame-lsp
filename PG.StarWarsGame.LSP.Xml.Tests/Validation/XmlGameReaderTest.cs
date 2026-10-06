// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     The emulation of the game's own XML reader. Every rule here was read out of the game's
///     reader; where the two games differ, the stricter one (FoC) is the rule.
/// </summary>
public sealed class XmlGameReaderTest
{
    private const string Decl = "<?xml version=\"1.0\"?>\n";

    private static XmlGameReadResult Read(string body) => XmlGameReader.Read(Decl + body);

    // ── files the game reads ────────────────────────────────────────────────

    [Fact]
    public void WellFormedFile_NoErrorNoTolerance()
    {
        var r = Read("<Root>\n\t<A Name=\"x\">1</A>\n\t<B/>\n</Root>\n");
        Assert.Null(r.Error);
        Assert.Empty(r.Tolerances);
    }

    [Fact]
    public void JunkBeforeTheDeclaration_IsSkipped()
    {
        // A UTF-8 byte order mark decoded as U+FEFF sits before the first '<'.
        var r = XmlGameReader.Read("﻿" + Decl + "<Root><A>1</A></Root>");
        Assert.Null(r.Error);
    }

    [Fact]
    public void DeclarationEndingInAngleBracketOnly_IsReadByTheGame()
    {
        // The game skips the declaration to the first '>'.
        var r = XmlGameReader.Read("<?xml version=\"1.0\">\n<Root><A>1</A></Root>");
        Assert.Null(r.Error);
    }

    [Fact]
    public void StrayAmpersand_IsReadByTheGame()
    {
        Assert.Null(Read("<Root><A>Tom & Jerry</A></Root>").Error);
    }

    // ── files the game drops ────────────────────────────────────────────────

    [Fact]
    public void MissingDeclaration_DropsTheFile()
    {
        var r = XmlGameReader.Read("<Root><A>1</A></Root>");
        Assert.Equal(XmlStrictnessCategory.MissingDeclaration, r.Error?.Category);
        Assert.Equal(0, r.Error!.Line);
    }

    [Fact]
    public void EndTagNamingAnotherElement_DropsTheFile_AtTheEndTag()
    {
        var r = Read("<Root>\n\t<Top_Color>1</Bottom_Color>\n</Root>");
        var e = Assert.IsType<XmlStructureError>(r.Error);
        Assert.Equal(XmlStrictnessCategory.EndTagMismatch, e.Category);
        Assert.Equal(2, e.Line);
        Assert.Equal("\t<Top_Color>1".Length, e.Column);
        Assert.Equal("</Bottom_Color>".Length, e.Length);
        Assert.Contains("Top_Color", e.Reason);
        Assert.Contains("Bottom_Color", e.Reason);
    }

    [Fact]
    public void EndTagDifferingInCaseOnly_DropsTheFile()
    {
        var r = Read("<Root><SoundFX_Name>x</SoundFX_name></Root>");
        Assert.Equal(XmlStrictnessCategory.EndTagCaseMismatch, r.Error?.Category);
    }

    [Fact]
    public void EndTagWhereAStartTagIsExpected_DropsTheFile()
    {
        // Inside an element '</' is read as an end tag (and mismatches); only where the root's
        // start tag is expected is it out of place.
        var r = Read("</Root>\n<Root><A>1</A></Root>");
        Assert.Equal(XmlStrictnessCategory.StrayEndTag, r.Error?.Category);
    }

    [Fact]
    public void RootWithoutChildren_DropsTheFile()
    {
        var r = Read("<Root>\n\t<!-- nothing -->\n</Root>");
        Assert.Equal(XmlStrictnessCategory.EmptyRoot, r.Error?.Category);
    }

    [Fact]
    public void SecondRootElement_DropsTheFile()
    {
        var r = Read("<Root><A>1</A></Root>\n<Other><B>2</B></Other>");
        var e = Assert.IsType<XmlStructureError>(r.Error);
        Assert.Equal(XmlStrictnessCategory.MultipleRoots, e.Category);
        Assert.Equal(2, e.Line);
    }

    [Theory]
    [InlineData("<Root><A Name='x'>1</A></Root>")]
    [InlineData("<Root><A Name=x>1</A></Root>")]
    [InlineData("<Root><A Name \"x\">1</A></Root>")]
    public void AttributeWithoutEqualsOrDoubleQuotes_DropsTheFile(string body)
    {
        Assert.Equal(XmlStrictnessCategory.AttributeSyntax, Read(body).Error?.Category);
    }

    [Theory]
    [InlineData("<Root><!-- a -- b --><A>1</A></Root>")]
    [InlineData("<Root><A>1</A><!-- never closed")]
    public void DoubleHyphenOrUnclosedComment_DropsTheFile(string body)
    {
        Assert.Equal(XmlStrictnessCategory.CommentSyntax, Read(body).Error?.Category);
    }

    [Fact]
    public void UnclosedElement_DropsTheFile()
    {
        Assert.Equal(XmlStrictnessCategory.UnexpectedEndOfFile, Read("<Root><A>1</A>").Error?.Category);
    }

    // ── read, with something the document shows not read as shown ───────────

    [Fact]
    public void CharacterDataAfterAChild_IsToleratedAndReportedEveryTime()
    {
        var r = Read("<Root>\n\t<A>1</A>``\n\t<B>2</B>-->\n</Root>");
        Assert.Null(r.Error);
        var t = r.Tolerances.Where(x => x.Category == XmlStrictnessCategory.CharacterDataAfterChild).ToList();
        Assert.Equal(2, t.Count);
        Assert.Equal(2, t[0].Line);
        Assert.Equal("\t<A>1</A>".Length, t[0].Column);
        Assert.Equal("``".Length, t[0].Length);
    }

    [Fact]
    public void CommentInsideAValue_IsToleratedAndReported()
    {
        var r = Read("<Root>\n\t<List>A,\n\t\t<!--B,-->\n\t\tC</List>\n</Root>");
        Assert.Null(r.Error);
        var t = Assert.Single(r.Tolerances);
        Assert.Equal(XmlStrictnessCategory.CommentInsideValue, t.Category);
        Assert.Equal(3, t.Line);
        Assert.Equal(2, t.Column);
        Assert.Equal("<!--B,-->".Length, t.Length);
    }

    [Fact]
    public void TwoCommentsInARowInsideAValue_AreBothValueComments()
    {
        // Vanilla FoC Starbases.xml: the value goes on after the second comment.
        var r = Read("<Root>\n\t<List>A,\n\t\t<!--B,-->  <!-- note -->\n\t\tC</List>\n</Root>");
        Assert.Null(r.Error);
        Assert.Equal(2, r.Tolerances.Count(t => t.Category == XmlStrictnessCategory.CommentInsideValue));
    }

    [Fact]
    public void CommentBeforeTheFirstChild_IsNotAValueComment()
    {
        var r = Read("<Root>\n\t<!-- header -->\n\t<A>1</A>\n</Root>");
        Assert.Empty(r.Tolerances);
    }

    [Fact]
    public void CommentAfterTheValueBeforeTheEndTag_IsNotAValueComment()
    {
        // Nothing follows the comment, so the value the game reads is the text before it.
        var r = Read("<Root><A>1 <!-- one --></A></Root>");
        Assert.Empty(r.Tolerances);
    }

    [Fact]
    public void NonBreakingSpaceAtTheEndOfAValue_IsNotTrimmedByTheGame()
    {
        // The game trims space, tab, CR and LF only.
        var r = Read("<Root>\n\t<Model>a.alo </Model>\n</Root>");
        var t = Assert.Single(r.Tolerances);
        Assert.Equal(XmlStrictnessCategory.UntrimmedValueCharacter, t.Category);
        Assert.Equal(2, t.Line);
        Assert.Equal("\t<Model>a.alo".Length, t.Column);
        Assert.Equal(1, t.Length);
    }
}