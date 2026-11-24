// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Localisation.Tests;

/// <summary>
///     Which file of a set of single-language files a staged change goes to. The grid shows such a
///     set as one table; taking a batch apart again is the server's, so no editor decides which
///     file a change is written to.
/// </summary>
public sealed class TranslationSetPartitionTest
{
    private static readonly IReadOnlyList<IReadOnlyList<string>> EnglishAndGerman = [["ENGLISH"], ["GERMAN"]];

    private static LocKeyedCommandDto SetValue(string key, string language, string value)
    {
        return new LocKeyedCommandDto("setValue", key, Language: language, Value: value);
    }

    private static List<List<string?>> Keys(
        IReadOnlyList<IReadOnlyList<(int Index, LocKeyedCommandDto Command)>> parts)
    {
        return parts.Select(p => p.Select(c => c.Command.Key).ToList()).ToList();
    }

    [Fact]
    public void AValue_GoesToTheFileHoldingItsLanguage()
    {
        var parts = TranslationSetPartition.Split(
            [SetValue("TEXT_A", "GERMAN", "Alfa"), SetValue("TEXT_B", "ENGLISH", "Beta")], EnglishAndGerman);

        Assert.Equal([["TEXT_B"], ["TEXT_A"]], Keys(parts));
    }

    [Fact]
    public void ALanguageIsMatchedWithoutRegardToCase()
    {
        var parts = TranslationSetPartition.Split([SetValue("TEXT_A", "german", "Alfa")], EnglishAndGerman);

        Assert.Equal([[], ["TEXT_A"]], Keys(parts));
    }

    // Dropped rather than guessed at: writing it into an arbitrary file would put German text in
    // the French file.
    [Fact]
    public void AValueForALanguageNoFileHolds_GoesNowhere()
    {
        var parts = TranslationSetPartition.Split([SetValue("TEXT_A", "KLINGON", "x")], EnglishAndGerman);

        Assert.All(parts, Assert.Empty);
    }

    [Fact]
    public void ANewEntry_GoesToEveryFile_WithTheValuesOfThatFilesLanguagesOnly()
    {
        var parts = TranslationSetPartition.Split(
        [
            new LocKeyedCommandDto("addEntry", "TEXT_C",
                Values: [new LocValueDto("ENGLISH", "Gamma"), new LocValueDto("GERMAN", "Gamma DE")])
        ], EnglishAndGerman);

        Assert.Equal([new LocValueDto("ENGLISH", "Gamma")], Assert.Single(parts[0]).Command.Values);
        Assert.Equal([new LocValueDto("GERMAN", "Gamma DE")], Assert.Single(parts[1]).Command.Values);
    }

    [Theory]
    [InlineData("renameKey")]
    [InlineData("deleteEntry")]
    public void AChangeOfTheTablesShape_GoesToEveryFile(string kind)
    {
        var parts = TranslationSetPartition.Split(
            [new LocKeyedCommandDto(kind, "TEXT_A", "TEXT_RENAMED")], EnglishAndGerman);

        Assert.Equal([["TEXT_A"], ["TEXT_A"]], Keys(parts));
    }

    // Another language is another file in a set, which the navigator creates; it is no column.
    [Fact]
    public void ANewLanguage_GoesNowhereInASet()
    {
        var parts = TranslationSetPartition.Split(
            [new LocKeyedCommandDto("addLanguage", null, Language: "FRENCH")], EnglishAndGerman);

        Assert.All(parts, Assert.Empty);
    }

    [Fact]
    public void EachCommandKeepsThePositionItHadInTheBatch()
    {
        var parts = TranslationSetPartition.Split(
        [
            SetValue("TEXT_A", "GERMAN", "one"),
            SetValue("TEXT_B", "ENGLISH", "two"),
            new LocKeyedCommandDto("deleteEntry", "TEXT_C"),
            SetValue("TEXT_D", "GERMAN", "four")
        ], EnglishAndGerman);

        Assert.Equal([1, 2], parts[0].Select(p => p.Index));
        Assert.Equal([0, 2, 3], parts[1].Select(p => p.Index));
    }

    /// <summary>
    ///     One file is not a set. It holds every language itself, so it takes the batch as staged -
    ///     a new language column, and a value for a language it does not hold yet, included.
    /// </summary>
    [Fact]
    public void OneFile_TakesTheWholeBatchAsItIs()
    {
        LocKeyedCommandDto[] batch =
        [
            new("addLanguage", null, Language: "FRENCH"),
            SetValue("TEXT_A", "FRENCH", "Alpha FR")
        ];

        var parts = TranslationSetPartition.Split(batch, [["ENGLISH", "GERMAN"]]);

        Assert.Equal(batch, Assert.Single(parts).Select(p => p.Command));
    }
}