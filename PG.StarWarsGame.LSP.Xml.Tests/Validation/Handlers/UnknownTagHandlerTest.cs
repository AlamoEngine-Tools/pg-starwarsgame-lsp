// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     What the rule says when a tag has no parser row.
/// </summary>
/// <remarks>
///     The rule itself is settled: all 150 (type, tag) pairs it fires on across <c>eaw/</c> - 1068
///     warnings - were checked against BOTH the engine's own exported parameter table
///     (<c>DatabaseMapExport.xml</c>, 123 tables, 2756 parameters) and the 2018 binary's
///     registration strings, and neither accepts a single one of them. The count is loud and
///     correct; these tests cover how it reads.
/// </remarks>
public sealed class UnknownTagHandlerTest
{
    private static readonly UnknownTagHandler Sut = new();

    private static readonly DiagnosticsContext Ctx =
        new(new EmptySchemaProvider(), Core.Symbols.GameIndex.Empty, "file:///test.xml", "en");

    private static XmlUnknownTagFact Fact(string? suggestion)
    {
        return new XmlUnknownTagFact("file:///test.xml", 0, 0, 10,
            "Autoresolve_Health", "GroundCompany", suggestion);
    }

    /// <summary>
    ///     Two sentences, so both of them keep their stop. The second was running unpunctuated
    ///     against a rule the repo states for every string a reader sees.
    /// </summary>
    [Fact]
    public void WithoutASuggestion_BothSentencesAreTerminated()
    {
        var d = Assert.Single(Sut.Handle(Fact(null), Ctx));

        Assert.Equal(
            "<Autoresolve_Health> is not a known GroundCompany tag. The engine discards it.",
            d.Message);
    }

    [Fact]
    public void WithASuggestion_AsksTheQuestionInstead()
    {
        var d = Assert.Single(Sut.Handle(Fact("Autoresolve_Health_Percentage"), Ctx));

        Assert.Equal(
            "<Autoresolve_Health> is not a known GroundCompany tag. " +
            "Did you mean <Autoresolve_Health_Percentage>?",
            d.Message);
    }

    /// <summary>
    ///     Warning, never error: a tag added after the build we mapped would look identical from
    ///     here, so being unknown to US is weaker evidence than being unknown to the engine.
    /// </summary>
    [Fact]
    public void IsAWarning()
    {
        var d = Assert.Single(Sut.Handle(Fact(null), Ctx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.UnknownTag, d.Id ?? Sut.DefaultId);
    }
}
