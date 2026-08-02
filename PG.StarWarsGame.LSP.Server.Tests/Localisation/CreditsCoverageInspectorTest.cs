// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class CreditsCoverageInspectorTest
{
    private static readonly string[] Languages = ["ENGLISH", "GERMAN"];

    private static LocRowDto Row(int index, string key, string english, string german)
    {
        return new LocRowDto(index, key,
            [new LocValueDto("ENGLISH", english), new LocValueDto("GERMAN", german)]);
    }

    [Fact]
    public void SectionsWithContentInBothLanguages_ReportNothing()
    {
        var rows = new[]
        {
            Row(0, "HEADER", "Lead Designer", "Chefdesigner"),
            Row(1, "CENTER", "Alice", "Alice"),
        };

        Assert.Empty(CreditsCoverageInspector.Inspect(rows, Languages));
    }

    // The one that is wrong in any language: the heading is translated, the names under it are not,
    // so the German crawl reads "Sprecher" and moves straight on.
    [Fact]
    public void AHeadingWithNothingUnderIt_IsReported()
    {
        var rows = new[]
        {
            Row(0, "HEADER", "Voice Cast", "Sprecher"),
            Row(1, "CENTER", "Alice", ""),
            Row(2, "CENTER", "Bob", ""),
        };

        var problem = Assert.Single(CreditsCoverageInspector.Inspect(rows, Languages));
        Assert.Equal("GERMAN", problem.Language);
        Assert.Equal("Sprecher", problem.Label);
    }

    // The user's case: a German dub recorded with fewer actors is a shorter list, not a broken one.
    // As long as the section has someone in it, nothing is wrong.
    [Fact]
    public void AShorterCastIsNotAProblem()
    {
        var rows = new[]
        {
            Row(0, "HEADER", "Voice Cast", "Sprecher"),
            Row(1, "CENTER", "Alice", "Anja"),
            Row(2, "CENTER", "Bob", ""),
            Row(3, "CENTER", "Carol", ""),
        };

        Assert.Empty(CreditsCoverageInspector.Inspect(rows, Languages));
    }

    // Leaving the heading empty too is how you skip a whole section in one language - the export
    // drops all of it and the crawl never mentions it.
    [Fact]
    public void ASectionSkippedEntirely_IsNotAProblem()
    {
        var rows = new[]
        {
            Row(0, "HEADER", "Voice Cast", ""),
            Row(1, "CENTER", "Alice", ""),
        };

        Assert.Empty(CreditsCoverageInspector.Inspect(rows, Languages));
    }

    // A language the file does not carry at all is absent, not half-done.
    [Fact]
    public void ALanguageWithNoTextAnywhere_IsNotReportedSectionBySection()
    {
        var rows = new[]
        {
            Row(0, "HEADER", "Voice Cast", ""),
            Row(1, "CENTER", "Alice", ""),
            Row(2, "HEADER", "Art", ""),
            Row(3, "CENTER", "Bob", ""),
        };

        Assert.Empty(CreditsCoverageInspector.Inspect(rows, Languages));
    }

    // Spacers are not content: a section of blank lines is still a heading with nothing under it.
    [Fact]
    public void ASectionOfOnlySpacers_IsReported()
    {
        var rows = new[]
        {
            Row(0, "HEADER", "Voice Cast", "Sprecher"),
            Row(1, "CENTER", "[TBL]", "[TBL]"),
            Row(2, "HEADER", "Art", "Grafik"),
            Row(3, "CENTER", "Bob", "Bob"),
        };

        // Both languages, because the section is only spacers in both - a spacer is not content.
        var problems = CreditsCoverageInspector.Inspect(rows, Languages);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Language == "GERMAN" && p.Label == "Sprecher");
        Assert.Contains(problems, p => p.Language == "ENGLISH" && p.Label == "Voice Cast");
    }

    [Fact]
    public void TheLastSectionIsCheckedToo()
    {
        var rows = new[]
        {
            Row(0, "HEADER", "Art", "Grafik"),
            Row(1, "CENTER", "Bob", "Bob"),
            Row(2, "HEADER", "Voice Cast", "Sprecher"),
        };

        // Trailing in both languages, so both are reported.
        var problems = CreditsCoverageInspector.Inspect(rows, Languages);
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Equal(2, p.Index));
    }

    // A file using its own directives reports nothing rather than guessing at a structure it cannot
    // see - no false positives on a vocabulary this does not know.
    [Fact]
    public void AFileWithItsOwnDirectives_ReportsNothing()
    {
        var rows = new[]
        {
            Row(0, "TITLE", "Voice Cast", "Sprecher"),
            Row(1, "LINE", "Alice", ""),
        };

        Assert.Empty(CreditsCoverageInspector.Inspect(rows, Languages));
    }
}
