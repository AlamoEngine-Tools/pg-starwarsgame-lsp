// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     Which localisation files are ordered credits files rather than keyed text files.
///     <para>
///         The distinction is not cosmetic: credits files allow duplicate keys and their row order
///         is significant, so classifying one as text would let a key-addressed edit collapse rows
///         the engine needs. Getting this wrong loses data, which is why the opt-out exists.
///     </para>
/// </summary>
public sealed class CreditsFileClassifierTest
{
    private static string Classify(string fileName, LocalisationCreditsSettings? settings = null)
    {
        return CreditsFileClassifier.Classify($"/ws/data/text/{fileName}", settings);
    }

    // ── convention (the default) ─────────────────────────────────────────────

    [Theory]
    [InlineData("creditstext.csv")]
    [InlineData("creditstext_english.csv")]
    [InlineData("credits.csv")]
    [InlineData("CreditsText_English.CSV")]
    [InlineData("CREDITSTEXT.XML")]
    public void ByConvention_ACreditsName_IsCredits(string fileName)
    {
        Assert.Equal(LocCategory.Credits, Classify(fileName));
    }

    [Theory]
    [InlineData("MasterTextFile.csv")]
    [InlineData("mastertextfile_english.csv")]
    [InlineData("gamecredits.csv")] // only a leading "credits" counts - this is someone else's file
    [InlineData("text.csv")]
    public void ByConvention_AnythingElse_IsText(string fileName)
    {
        Assert.Equal(LocCategory.Text, Classify(fileName));
    }

    [Fact]
    public void AbsentSettings_BehaveAsConvention()
    {
        Assert.Equal(LocCategory.Credits, CreditsFileClassifier.Classify("/ws/creditstext.csv", null));
    }

    // ── none: the opt-out ────────────────────────────────────────────────────

    // A mod whose MasterText happens to be named creditstext must be able to say so, or the editor
    // would refuse to treat its only text file as text.
    [Fact]
    public void DetectionNone_OverridesTheConvention()
    {
        var settings = new LocalisationCreditsSettings(LocalisationCreditsSettings.None, []);

        Assert.Equal(LocCategory.Text, Classify("creditstext.csv", settings));
    }

    // ── explicit ─────────────────────────────────────────────────────────────

    [Fact]
    public void DetectionExplicit_ListsCreditsAndIgnoresTheConvention()
    {
        var settings = new LocalisationCreditsSettings(
            LocalisationCreditsSettings.Explicit, ["rolls.csv"]);

        Assert.Equal(LocCategory.Credits, Classify("rolls.csv", settings));
        Assert.Equal(LocCategory.Text, Classify("creditstext.csv", settings));
    }

    [Fact]
    public void DetectionExplicit_MatchesCaseInsensitivelyAndWithoutTheExtension()
    {
        var settings = new LocalisationCreditsSettings(
            LocalisationCreditsSettings.Explicit, ["ROLLS.CSV", "scroll"]);

        Assert.Equal(LocCategory.Credits, Classify("rolls.csv", settings));
        Assert.Equal(LocCategory.Credits, Classify("scroll.csv", settings));
    }

    // ── convention + explicit list = union ───────────────────────────────────

    [Fact]
    public void ConventionWithFiles_IsTheUnionOfBoth()
    {
        var settings = new LocalisationCreditsSettings(
            LocalisationCreditsSettings.Convention, ["rolls.csv"]);

        Assert.Equal(LocCategory.Credits, Classify("creditstext.csv", settings));
        Assert.Equal(LocCategory.Credits, Classify("rolls.csv", settings));
        Assert.Equal(LocCategory.Text, Classify("MasterTextFile.csv", settings));
    }

    // The list is meaningless under "none" - the opt-out has to be absolute, or it would be a
    // confusing half-measure that silently keeps some files classified.
    [Fact]
    public void DetectionNone_IgnoresTheFileList()
    {
        var settings = new LocalisationCreditsSettings(LocalisationCreditsSettings.None, ["rolls.csv"]);

        Assert.Equal(LocCategory.Text, Classify("rolls.csv", settings));
    }
}
