// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Tests.Diagnostics;

/// <summary>
///     The id has to survive the trip to the client: it is what the editor shows next to the
///     message and what a suppression quick fix writes into the file.
/// </summary>
public sealed class DiagnosticCodeWiringTest
{
    private sealed record ProbeFact(string DocumentUri, int Line, int Column, int Length)
        : XmlFact(DocumentUri, Line, Column, Length);

    [Fact]
    public void ResultWithId_PublishesItAsTheDiagnosticCode()
    {
        var fact = new ProbeFact("file:///x.xml", 3, 4, 5);
        var result = new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Error, "boom", Id: DiagnosticIds.DuplicateSymbol);

        var diagnostic = XmlDiagnosticsPublisher.ToLspDiagnosticForTest(fact, result);

        Assert.Equal("aetswg-010-0001", diagnostic.Code?.String);
    }

    // Until every handler carries an id (#68), an id-less result must still publish - just
    // without a code - rather than crashing or inventing one.
    [Fact]
    public void ResultWithoutId_PublishesWithoutACode()
    {
        var fact = new ProbeFact("file:///x.xml", 3, 4, 5);
        var result = new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, "boom");

        var diagnostic = XmlDiagnosticsPublisher.ToLspDiagnosticForTest(fact, result);

        Assert.Null(diagnostic.Code);
    }

    [Fact]
    public void Id_IsCarriedOnTheResultRecord()
    {
        var result = new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Warning, "m", Id: DiagnosticIds.StoryChain);

        Assert.Equal(DiagnosticIds.StoryChain, result.Id);
    }
}
