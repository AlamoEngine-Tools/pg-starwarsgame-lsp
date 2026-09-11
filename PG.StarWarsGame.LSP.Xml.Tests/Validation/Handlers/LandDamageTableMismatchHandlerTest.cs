// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class LandDamageTableMismatchHandlerTest
{
    private static readonly LandDamageTableMismatchHandler Sut = new();

    [Fact]
    public void The_message_names_both_counts_and_the_surplus()
    {
        var d = Single(["1", "0.66", "0.33", "0"], ["0", "1", "2"]);

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Land_Damage_Thresholds", d.Message);
        Assert.Contains("Land_Damage_Alternates", d.Message);
        Assert.Contains("4", d.Message);
        Assert.Contains("3", d.Message);
    }

    // The surplus sits in whichever column is longer, so the diagnostic has to move: reporting a
    // spare alternate on the thresholds tag points the reader at the wrong line.
    [Fact]
    public void The_diagnostic_lands_on_the_longer_column()
    {
        var thresholdsLonger = Single(["1", "0.5", "0.25"], ["0", "1"]);
        Assert.Equal(11, thresholdsLonger.OverrideLine);
        Assert.Equal(4, thresholdsLonger.OverrideColumn);
        Assert.Equal(20, thresholdsLonger.OverrideLength);

        var alternatesLonger = Single(["1", "0.5"], ["0", "1", "2"]);
        Assert.Equal(22, alternatesLonger.OverrideLine);
    }

    [Fact]
    public void The_quick_fix_trims_the_longer_column_to_the_shorter_length()
    {
        Assert.Equal("1, 0.66, 0.33", Single(["1", "0.66", "0.33", "0"], ["0", "1", "2"]).SuggestedFix);
        Assert.Equal("0, 1", Single(["1", "0.5"], ["0", "1", "2"]).SuggestedFix);
    }

    // Trimming the other column to nothing is not a fix, it is emptying the table, so the
    // mismatch is still reported and the reader is left to write the missing entries.
    [Fact]
    public void An_empty_column_is_reported_but_offers_no_trim()
    {
        var d = Single([], ["0", "1"]);

        Assert.Contains("0", d.Message);
        Assert.Null(d.SuggestedFix);
    }

    [Fact]
    public void The_handler_carries_its_diagnostic_id()
    {
        Assert.Equal(DiagnosticIds.LandDamageTableMismatch, Sut.DefaultId);
    }

    private static XmlDiagnosticResult Single(string[] thresholds, string[] alternates)
    {
        var fact = new LandDamageTableMismatchFact(
            "file:///test.xml", 0, 0, 8,
            thresholds, alternates,
            (11, 4, 20),
            (22, 4, 20));

        return Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList());
    }
}
