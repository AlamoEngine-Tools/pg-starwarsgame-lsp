// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     The repair for an end tag the game rejects. Each test deletes or damages one tag of a
///     well-formed document; the repair must give that document back.
/// </summary>
public sealed class XmlEndTagRepairTest
{
    private const string Decl = "<?xml version=\"1.0\"?>\n";

    private static XmlStructureError Error(string text)
    {
        var error = XmlGameReader.Read(text).Error;
        Assert.NotNull(error);
        return error;
    }

    private static string Repaired(string text)
    {
        var repair = Error(text).Repair;
        Assert.NotNull(repair);
        return XmlStructureRepairs.Apply(text, repair);
    }

    [Fact]
    public void ChildrenAfterAnUnclosedElement_TheElementIsClosed_NotTheParentRenamed()
    {
        const string original = Decl + "<Root>\n\t<Data>\n\t\t<!-- Primary ability -->\n\t\t<Ability>\n" +
                                "\t\t\t<Type>SPREAD_OUT</Type>\n\t\t\t<Speed>0.5</Speed>\n\t\t</Ability>\n\t</Data>\n</Root>";
        var broken = original.Replace("\t\t</Ability>\n", "", StringComparison.Ordinal);

        var error = Error(broken);

        Assert.Equal(XmlStrictnessCategory.EndTagMismatch, error.Category);
        Assert.Contains("<Ability> is never closed", error.Reason);
        Assert.Equal(4, error.Line); // anchored on the unclosed start tag
        Assert.Equal("Close <Ability>", error.Repair!.Title);
        Assert.Equal(original, Repaired(broken));
    }

    [Fact]
    public void AnUnclosedElementFollowedByASibling_IsClosedBeforeTheSibling()
    {
        const string original = Decl + "<Root>\n\t<Data>\n\t\t<Ability>\n\t\t\t<Type>A</Type>\n\t\t</Ability>\n" +
                                "\t\t<Ability>\n\t\t\t<Type>B</Type>\n\t\t</Ability>\n\t</Data>\n</Root>";
        var first = original.IndexOf("\t\t</Ability>\n", StringComparison.Ordinal);
        var broken = original.Remove(first, "\t\t</Ability>\n".Length);

        Assert.Equal(original, Repaired(broken));
    }

    [Fact]
    public void AnUnclosedOneLineValue_IsClosedOnItsLine()
    {
        const string original = Decl + "<Root>\n\t<Unit>\n\t\t<A>1</A>\n\t\t<B>2</B>\n\t</Unit>\n</Root>";
        var broken = original.Replace("1</A>", "1", StringComparison.Ordinal);

        Assert.Equal(original, Repaired(broken));
    }

    /// <summary>
    ///     Vanilla is not consistently indented: campaign children sit at two and three tabs in one
    ///     element. A value says more than the layout - an element holding one is a leaf.
    /// </summary>
    [Fact]
    public void AnUnclosedElementWithAValue_IsALeaf_WhateverTheIndentationSays()
    {
        const string original = Decl + "<Root>\n\t<Campaign>\n\t\t<Markup>Empire, Hints </Markup>\n" +
                                "\t\t<Markup>Rebel, Default </Markup>\n\t\t\t<Forces> Empire, Fondor </Forces>\n" +
                                "\t\t\t<Forces> Empire, Kessel </Forces>\n\t</Campaign>\n</Root>";
        var first = original.IndexOf("</Markup>", StringComparison.Ordinal);
        var broken = original.Remove(first, "</Markup>".Length);

        Assert.Equal(original.Replace("Hints </Markup>", "Hints</Markup> ", StringComparison.Ordinal),
            Repaired(broken));
    }

    /// <summary>Story files indent later events deeper; an element still never contains its own name.</summary>
    [Fact]
    public void AnUnclosedElement_IsClosedBeforeTheNextElementOfItsName_WhateverTheIndentationSays()
    {
        const string original = Decl + "<Story>\n   <Event Name=\"A\">\n      <Type>X</Type>\n   </Event>\n" +
                                "         <Event Name=\"B\">\n            <Type>Y</Type>\n         </Event>\n</Story>";
        var first = original.IndexOf("   </Event>\n", StringComparison.Ordinal);
        var broken = original.Remove(first, "   </Event>\n".Length);

        Assert.Equal(original, Repaired(broken));
    }

    /// <summary>
    ///     Props files name the root like its children, so the root's end tag closes the unclosed
    ///     child and the file ends with the root open.
    /// </summary>
    [Fact]
    public void RootNamedLikeItsChildren_TheUnclosedChildIsClosed_NotTheRoot()
    {
        const string original = Decl + "<Props>\n\t<Props Name=\"A\">\n\t\t<Scale>1</Scale>\n\t</Props>\n" +
                                "\t<Props Name=\"B\">\n\t\t<Scale>2</Scale>\n\t</Props>\n</Props>";
        var first = original.IndexOf("\t</Props>\n", StringComparison.Ordinal);
        var broken = original.Remove(first, "\t</Props>\n".Length);

        var error = Error(broken);

        Assert.Equal(XmlStrictnessCategory.UnexpectedEndOfFile, error.Category);
        Assert.Equal(original, Repaired(broken));
    }

    [Fact]
    public void TheRootLeftOpen_IsClosedAtTheEndOfTheFile()
    {
        const string original = Decl + "<Root>\n\t<A>1</A>\n</Root>\n";
        var broken = original.Replace("</Root>\n", "", StringComparison.Ordinal);

        Assert.Equal(original, Repaired(broken));
    }

    /// <summary>
    ///     Vanilla writes some children flush with their parent (the B-wing's ability). The layout
    ///     says nothing then; the other elements of the name do.
    /// </summary>
    [Fact]
    public void ChildrenFlushWithTheUnclosedElement_StayInside_WhenOtherElementsOfTheNameAreContainers()
    {
        const string original = Decl + "<Root>\n\t<Unit>\n\t\t<Data>\n\t\t\t<Ability>\n\t\t\t<Type>LOCK</Type>\n" +
                                "\t\t\t<Speed>3.0</Speed>\n\t\t\t</Ability>\n\t\t</Data>\n\t</Unit>\n" +
                                "\t<Unit>\n\t\t<Data>\n\t\t\t<Ability>\n\t\t\t\t<Type>SPREAD</Type>\n\t\t\t</Ability>\n" +
                                "\t\t</Data>\n\t</Unit>\n</Root>";
        var first = original.IndexOf("\t\t\t</Ability>\n", StringComparison.Ordinal);
        var broken = original.Remove(first, "\t\t\t</Ability>\n".Length);

        Assert.Equal(original, Repaired(broken));
    }

    [Fact]
    public void AnEmptyLeafLeftOpen_IsClosedEmpty_WhenOtherElementsOfTheNameAreLeaves()
    {
        const string original = Decl + "<Root>\n\t<Event>\n\t\t<Param></Param>\n\t\t<Type>X</Type>\n\t</Event>\n" +
                                "\t<Event>\n\t\t<Param>1</Param>\n\t\t<Type>Y</Type>\n\t</Event>\n</Root>";
        var first = original.IndexOf("</Param>", StringComparison.Ordinal);
        var broken = original.Remove(first, "</Param>".Length);

        Assert.Equal(original, Repaired(broken));
    }

    [Fact]
    public void TwoUnclosedLevels_BothAreClosed()
    {
        const string original = Decl + "<Root>\n\t<Unit>\n\t\t<Data>\n\t\t\t<A>1</A>\n\t\t</Data>\n\t</Unit>\n</Root>";
        var broken = original.Replace("\t\t</Data>\n\t</Unit>\n", "", StringComparison.Ordinal);

        var error = Error(broken);

        Assert.Equal("Close <Data> and <Unit>", error.Repair!.Title);
        Assert.Equal(original, Repaired(broken));
    }

    [Fact]
    public void AnEndTagNamingNothingOpen_IsRenamed()
    {
        const string original = Decl + "<Root>\n\t<Unit>\n\t\t<A>1</A>\n\t</Unit>\n</Root>";
        var broken = original.Replace("</Unit>", "</Unti>", StringComparison.Ordinal);

        var error = Error(broken);

        Assert.Equal("Rename the end tag to </Unit>", error.Repair!.Title);
        Assert.Equal(original, Repaired(broken));
    }

    [Fact]
    public void AnExtraEndTagNamingNothingOpen_IsRemoved()
    {
        const string original = Decl + "<Root>\n\t<Unit>\n\t\t<A>1</A>\n\t</Unit>\n</Root>";
        var broken = original.Replace("1</A>", "1</A></B>", StringComparison.Ordinal);

        var error = Error(broken);

        Assert.Equal("Remove the end tag", error.Repair!.Title);
        Assert.Equal(original, Repaired(broken));
    }

    [Fact]
    public void NoIndentationToGoBy_TheElementKeepsEverythingTheGameReadAsItsContent()
    {
        const string original = Decl + "<Root><Unit><A>1</A><B>2</B></Unit></Root>";
        var broken = original.Replace("</Unit>", "", StringComparison.Ordinal);

        Assert.Equal(original, Repaired(broken));
    }

    [Fact]
    public void AnEndTagDifferingInCaseOnly_IsStillRecased()
    {
        const string original = Decl + "<Root>\n\t<Unit>1</Unit>\n</Root>";
        var broken = original.Replace("</Unit>", "</unit>", StringComparison.Ordinal);

        var error = Error(broken);

        Assert.Equal(XmlStrictnessCategory.EndTagCaseMismatch, error.Category);
        Assert.Equal(original, Repaired(broken));
    }
}