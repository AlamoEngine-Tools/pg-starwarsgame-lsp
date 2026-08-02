// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class LanguageCoverageInspectorTest
{
    private static readonly string[] Languages = ["ENGLISH", "GERMAN"];

    private static LocRowDto Row(int index, string key, string? english, string? german)
    {
        var values = new List<LocValueDto>();
        if (english is not null) values.Add(new LocValueDto("ENGLISH", english));
        if (german is not null) values.Add(new LocValueDto("GERMAN", german));
        return new LocRowDto(index, key, values);
    }

    [Fact]
    public void FullyTranslated_ReportsNothing()
    {
        var rows = new[] { Row(0, "CENTER", "Alice", "Alice"), Row(1, "HEADER", "Lead", "Chef") };

        Assert.Empty(LanguageCoverageInspector.Inspect(rows, Languages));
    }

    [Fact]
    public void CountsTheEntriesALanguageHasNotFilledIn()
    {
        var rows = new[]
        {
            Row(0, "HEADER", "Lead", "Chef"),
            Row(1, "CENTER", "Alice", ""),
            Row(2, "CENTER", "Bob", null),
        };

        var problem = Assert.Single(LanguageCoverageInspector.Inspect(rows, Languages));
        Assert.Equal("GERMAN", problem.Language);
        Assert.Equal(2, problem.Empty);
        Assert.Equal(3, problem.Total);
    }

    // A row blank everywhere is a spacer or an empty line - it is not evidence that any one
    // language is behind, and counting it would make every credits file look half-translated.
    [Fact]
    public void ARowBlankInEveryLanguage_IsNotCountedAgainstAnyone()
    {
        var rows = new[] { Row(0, "CENTER", "Alice", "Alice"), Row(1, "CENTER", "", "") };

        Assert.Empty(LanguageCoverageInspector.Inspect(rows, Languages));
    }

    [Fact]
    public void ReportsEachLanguageSeparately()
    {
        var rows = new[] { Row(0, "CENTER", "", "Alice"), Row(1, "CENTER", "Bob", "") };

        var problems = LanguageCoverageInspector.Inspect(rows, Languages);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Equal(1, p.Empty));
    }

    // One problem per language, not per entry: a half-translated MasterTextFile has tens of
    // thousands of gaps and would bury every other problem in the panel.
    [Fact]
    public void ManyGaps_StillProduceOneProblemPerLanguage()
    {
        var rows = Enumerable.Range(0, 5000).Select(i => Row(i, $"TEXT_{i}", "English", "")).ToList();

        var problem = Assert.Single(LanguageCoverageInspector.Inspect(rows, Languages));
        Assert.Equal(5000, problem.Empty);
    }

    // A single-language file is as long as it is; there is nothing to be behind.
    [Fact]
    public void SingleLanguageFile_ReportsNothing()
    {
        var rows = new[] { Row(0, "CENTER", "Alice", null), Row(1, "CENTER", "", null) };

        Assert.Empty(LanguageCoverageInspector.Inspect(rows, ["ENGLISH"]));
    }

    [Fact]
    public void WhitespaceOnly_CountsAsEmpty()
    {
        var rows = new[] { Row(0, "CENTER", "Alice", "   ") };

        Assert.Equal(1, Assert.Single(LanguageCoverageInspector.Inspect(rows, Languages)).Empty);
    }
}
