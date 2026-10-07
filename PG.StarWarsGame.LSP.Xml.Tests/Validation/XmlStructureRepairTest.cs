// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     Each repairable category, applied to the text it was found in: the exact text afterwards,
///     and the category gone on a second read. Where the game read the file, a repair keeps what
///     the game reads; where the game dropped it, the repair is what makes it readable.
/// </summary>
public sealed class XmlStructureRepairTest
{
    private const string Decl = "<?xml version=\"1.0\"?>\n";
    private static readonly XmlStructuralValidator Validator = new();

    private static string Repair(string text, XmlStrictnessCategory category)
    {
        var finding = Assert.Single(Validator.Validate(text), f => f.Category == category);
        var repair = Assert.IsType<XmlRepair>(finding.Repair);
        var repaired = XmlStructureRepairs.Apply(text, repair);
        Assert.DoesNotContain(Validator.Validate(repaired), f => f.Category == category);
        return repaired;
    }

    [Fact]
    public void MissingDeclaration_IsInserted()
    {
        Assert.Equal(Decl + "<Root><A>1</A></Root>",
            Repair("<Root><A>1</A></Root>", XmlStrictnessCategory.MissingDeclaration));
    }

    [Fact]
    public void MissingDeclaration_IsInsertedAfterAByteOrderMark()
    {
        Assert.Equal("﻿" + Decl + "<Root><A>1</A></Root>",
            Repair("﻿<Root><A>1</A></Root>", XmlStrictnessCategory.MissingDeclaration));
    }

    [Fact]
    public void EndTagMismatch_RenamesTheEndTagToTheStartTag()
    {
        Assert.Equal(Decl + "<Root>\n\t<Top_Color>1</Top_Color>\n</Root>",
            Repair(Decl + "<Root>\n\t<Top_Color>1</Bottom_Color>\n</Root>", XmlStrictnessCategory.EndTagMismatch));
    }

    [Fact]
    public void EndTagCaseMismatch_RecasesTheEndTag()
    {
        Assert.Equal(Decl + "<Root><SoundFX_Name>x</SoundFX_Name></Root>",
            Repair(Decl + "<Root><SoundFX_Name>x</SoundFX_name></Root>", XmlStrictnessCategory.EndTagCaseMismatch));
    }

    [Fact]
    public void StrayEndTag_IsRemoved()
    {
        Assert.Equal(Decl + "\n<Root><A>1</A></Root>",
            Repair(Decl + "</Root>\n<Root><A>1</A></Root>", XmlStrictnessCategory.StrayEndTag));
    }

    [Fact]
    public void SingleQuotedAttribute_GetsDoubleQuotes()
    {
        Assert.Equal(Decl + "<Root><A Name=\"x y\">1</A></Root>",
            Repair(Decl + "<Root><A Name='x y'>1</A></Root>", XmlStrictnessCategory.AttributeSyntax));
    }

    [Fact]
    public void SingleQuotedAttributeHoldingADoubleQuote_HasNoRepair()
    {
        var finding = Assert.Single(Validator.Validate(Decl + "<Root><A Name='say \"hi\"'>1</A></Root>"));
        Assert.Null(finding.Repair);
    }

    [Fact]
    public void DoubleHyphenInAComment_IsSpacedOut()
    {
        Assert.Equal(Decl + "<Root><!-- a - - b --><A>1</A></Root>",
            Repair(Decl + "<Root><!-- a -- b --><A>1</A></Root>", XmlStrictnessCategory.CommentSyntax));
    }

    [Fact]
    public void MalformedDeclaration_GetsItsQuestionMark()
    {
        Assert.Equal("<?xml version=\"1.0\"?>\n<Root><A>1</A></Root>",
            Repair("<?xml version=\"1.0\">\n<Root><A>1</A></Root>", XmlStrictnessCategory.MalformedDeclaration));
    }

    [Fact]
    public void CharacterDataAfterAChild_IsRemoved()
    {
        Assert.Equal(Decl + "<Root>\n\t<A>1</A>\n</Root>",
            Repair(Decl + "<Root>\n\t<A>1</A>``\n</Root>", XmlStrictnessCategory.CharacterDataAfterChild));
    }

    [Fact]
    public void UntrimmedValueCharacter_IsRemoved()
    {
        Assert.Equal(Decl + "<Root>\n\t<Model>a.alo</Model>\n</Root>",
            Repair(Decl + "<Root>\n\t<Model>a.alo </Model>\n</Root>", XmlStrictnessCategory.UntrimmedValueCharacter));
    }

    [Fact]
    public void CommentOnItsOwnLineInsideAValue_MovesAboveTheElement()
    {
        const string before = Decl + "<Root>\n\t<List>A,\n\t\t<!--B,-->\n\t\tC</List>\n</Root>";
        const string after = Decl + "<Root>\n\t<!--B,-->\n\t<List>A,\n\t\tC</List>\n</Root>";
        Assert.Equal(after, Repair(before, XmlStrictnessCategory.CommentInsideValue));
    }

    [Fact]
    public void CommentSharingALineInsideAValue_MovesAboveTheElement()
    {
        const string before = Decl + "<Root>\n\t<List>A, <!--B,--> C</List>\n</Root>";
        const string after = Decl + "<Root>\n\t<!--B,-->\n\t<List>A,  C</List>\n</Root>";
        Assert.Equal(after, Repair(before, XmlStrictnessCategory.CommentInsideValue));
    }

    [Theory]
    [InlineData("<Root>\n<!-- nothing -->\n</Root>")] // empty root
    [InlineData("<Root><A>1</A></Root>\n<Other><B>2</B></Other>")] // second root
    [InlineData("<Root><A>Tom & Jerry</A></Root>")] // stray ampersand: escaping would change the value
    public void CategoriesWithoutARepair_OfferNone(string body)
    {
        Assert.All(Validator.Validate(Decl + body), f => Assert.Null(f.Repair));
    }

    // ── fix-all ─────────────────────────────────────────────────────────────

    [Fact]
    public void FixAll_RepairsEveryRepairableFinding_UntilOnlyUnrepairableOnesRemain()
    {
        const string text = "<?xml version=\"1.0\">\n<Root>\n\t<A>1</B>\n\t<C Name='x'>2</C>``\n\t<D>Tom & Jerry</D>\n</Root>";

        var result = XmlStructureRepairs.FixAll(text, Validator, _ => true);

        Assert.Equal(
            "<?xml version=\"1.0\"?>\n<Root>\n\t<A>1</A>\n\t<C Name=\"x\">2</C>\n\t<D>Tom & Jerry</D>\n</Root>",
            result.Text);
        Assert.Equal([XmlStrictnessCategory.StrayAmpersand], Validator.Validate(result.Text).Select(f => f.Category));
    }

    [Fact]
    public void FixAll_LeavesExcludedCategoriesAlone()
    {
        const string text = Decl + "<Root>\n\t<List>A,\n\t\t<!--B,-->\n\t\tC</List>\n\t<A>1</A>``\n</Root>";

        var result = XmlStructureRepairs.FixAll(text, Validator,
            c => c != XmlStrictnessCategory.CommentInsideValue);

        Assert.Contains("<!--B,-->\n\t\tC", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("``", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FixAll_StopsAtTheIterationCap()
    {
        // The game stops at its first read error, so each mismatched end tag takes one pass.
        var body = string.Concat(Enumerable.Range(0, 10).Select(i => $"<A{i}>1</B{i}>"));
        var result = XmlStructureRepairs.FixAll(Decl + "<Root>" + body + "</Root>", Validator, _ => true, 4);

        Assert.Equal(4, result.Iterations);
        Assert.Contains(Validator.Validate(result.Text), f => f.Category == XmlStrictnessCategory.EndTagMismatch);
    }
}
