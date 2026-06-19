// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class VariantRedundantOverrideHandlerTest
{
    private static readonly VariantRedundantOverrideHandler Sut = new();

    private static VariantRedundantOverrideFact MakeFact()
    {
        return new VariantRedundantOverrideFact("file:///test.xml", 5, 4, 9, "Max_Speed");
    }

    [Fact]
    public void Emits_single_hint_diagnostic()
    {
        var results = Sut.Handle(MakeFact(), XmlHandlerTestFixtures.EmptyCtx).ToList();

        var result = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Hint, result.Severity);
    }

    [Fact]
    public void Marks_diagnostic_as_unnecessary()
    {
        var result = Sut.Handle(MakeFact(), XmlHandlerTestFixtures.EmptyCtx).Single();

        Assert.NotNull(result.Tags);
        Assert.Contains(XmlDiagnosticTag.Unnecessary, result.Tags!);
    }

    [Fact]
    public void Flags_diagnostic_for_redundant_override_removal()
    {
        var result = Sut.Handle(MakeFact(), XmlHandlerTestFixtures.EmptyCtx).Single();

        Assert.True(result.RemoveRedundantOverride);
    }

    [Fact]
    public void Message_names_the_tag()
    {
        var result = Sut.Handle(MakeFact(), XmlHandlerTestFixtures.EmptyCtx).Single();

        Assert.Contains("Max_Speed", result.Message);
    }
}