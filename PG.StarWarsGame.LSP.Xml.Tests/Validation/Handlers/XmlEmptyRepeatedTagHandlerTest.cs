// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     An empty repeat of a tag does nothing in the game: its field mapper returns before converting
///     an empty value, so the earlier occurrence - or the inherited value - stays. The author who
///     meant to clear the value has not, and is told so.
/// </summary>
public sealed class XmlEmptyRepeatedTagHandlerTest
{
    private static readonly XmlEmptyRepeatedTagHandler Sut = new();

    [Fact]
    public void An_empty_repeat_is_a_warning_naming_the_value_that_stays_and_its_line()
    {
        var fact = new XmlEmptyRepeatedTagFact("file:///test.xml", 4, 0, 11, "Icon_Name", "i_button_han_solo.tga", 3);

        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Icon_Name", d.Message);
        Assert.Contains("i_button_han_solo.tga", d.Message);
        Assert.Contains("line 3", d.Message);
    }

    /// <summary>A variant's lone empty tag: the value it keeps is the base's, which has no line in this file.</summary>
    [Fact]
    public void An_empty_tag_on_a_variant_names_the_inherited_value()
    {
        var fact = new XmlEmptyRepeatedTagFact("file:///test.xml", 4, 0, 11, "Icon_Name", "base.tga", null);

        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.Contains("inherited", d.Message);
        Assert.Contains("base.tga", d.Message);
        Assert.DoesNotContain("line", d.Message);
    }

    /// <summary>Dead text, greyed out like an earlier duplicate - but the remove-earlier fix would delete the wrong one.</summary>
    [Fact]
    public void An_empty_repeat_is_unnecessary_and_offers_no_remove_earlier_fix()
    {
        var fact = new XmlEmptyRepeatedTagFact("file:///test.xml", 4, 0, 11, "Icon_Name", "x.tga", 3);

        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.NotNull(d.Tags);
        Assert.Contains(XmlDiagnosticTag.Unnecessary, d.Tags);
        Assert.False(d.OfferRemoveEarlierDuplicates);
    }

    [Fact]
    public void The_id_is_its_own()
    {
        Assert.Equal(DiagnosticIds.XmlEmptyRepeatedTag, Sut.DefaultId);
    }
}