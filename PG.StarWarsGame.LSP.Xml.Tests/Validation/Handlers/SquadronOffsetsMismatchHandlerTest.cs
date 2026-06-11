// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class SquadronOffsetsMismatchHandlerTest
{
    private static readonly SquadronOffsetsMismatchHandler Sut = new();

    private static SquadronOffsetsMismatchFact MakeFact(int totalUnits, int totalOffsets)
    {
        return new SquadronOffsetsMismatchFact(
            "file:///test.xml", 0, 0, 8,
            totalUnits, totalOffsets,
            UnitTagLocations: [],
            OffsetTagLocations: []);
    }

    [Fact]
    public void Too_few_offsets_returns_warning_with_add_message()
    {
        var fact = MakeFact(totalUnits: 5, totalOffsets: 2);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("5", d.Message);
        Assert.Contains("2", d.Message);
        Assert.Contains("3", d.Message);
    }

    [Fact]
    public void Too_many_offsets_returns_warning_with_remove_message()
    {
        var fact = MakeFact(totalUnits: 3, totalOffsets: 7);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("3", d.Message);
        Assert.Contains("7", d.Message);
        Assert.Contains("4", d.Message);
    }

    [Fact]
    public void Zero_offsets_returns_warning_with_add_all_message()
    {
        var fact = MakeFact(totalUnits: 4, totalOffsets: 0);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("4", d.Message);
    }

    [Fact]
    public void Handler_sets_SquadronSyncJson_with_expected_offsets()
    {
        var fact = MakeFact(totalUnits: 5, totalOffsets: 2);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.NotNull(d.SquadronSyncJson);
        Assert.Contains("5", d.SquadronSyncJson);
    }
}
