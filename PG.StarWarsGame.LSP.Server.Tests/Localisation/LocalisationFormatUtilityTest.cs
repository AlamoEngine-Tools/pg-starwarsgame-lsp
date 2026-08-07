// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class LocalisationFormatUtilityTest
{
    [Theory]
    [InlineData("csv", ".csv")]
    [InlineData("xml", ".xml")]
    [InlineData("nls", ".properties")]
    [InlineData("dat", ".dat")]
    public void ToExtension_KnowsEveryFormatAProjectCanDeclare(string format, string expected)
    {
        Assert.Equal(expected, LocalisationFormatUtility.ToExtension(format));
    }

    // ModProjectLoader upper-cases the declared type, so this is the shape it is actually asked
    // about - a case-sensitive lookup here silently answered "unknown" for every real project.
    [Theory]
    [InlineData("DAT", ".dat")]
    [InlineData("CSV", ".csv")]
    [InlineData("Xml", ".xml")]
    public void ToExtension_IsCaseInsensitive(string format, string expected)
    {
        Assert.Equal(expected, LocalisationFormatUtility.ToExtension(format));
    }

    // DAT is deliberately absent from the converter's writable formats. It must NOT be absent here:
    // identifying the format a file already is, is a different question from whether one can be
    // written, and conflating them left a DAT project's .pgproj pointing at DAT after a conversion.
    [Fact]
    public void ToExtension_CoversDat_UnlikeTheWritableFormats()
    {
        Assert.Equal(".dat", LocalisationFormatUtility.ToExtension("dat"));
        Assert.Null(LocalisationFormatUtility.ToSeedFileName("dat"));
    }

    [Theory]
    [InlineData("yaml")]
    [InlineData("")]
    [InlineData(null)]
    public void ToExtension_UnknownFormat_IsNull(string? format)
    {
        Assert.Null(LocalisationFormatUtility.ToExtension(format));
    }

    // ── seed file names ──────────────────────────────────────────────────────

    /// <summary>
    ///     The engine's own files are lowercase - <c>mastertextfile_english.dat</c>,
    ///     <c>creditstext_english.dat</c> - and the game reads names case-insensitively. We wrote
    ///     PascalCase into the same folders, so a stock text directory ended up holding
    ///     <c>MasterTextFile.csv</c> next to <c>mastertextfile_english.dat</c>.
    /// </summary>
    [Theory]
    [InlineData("csv", "mastertextfile.csv")]
    [InlineData("xml", "mastertextfile.xml")]
    public void ToSeedFileName_MultiLanguageFormat_IsLowercaseAndCarriesNoLanguage(
        string format, string expected)
    {
        Assert.Equal(expected, LocalisationFormatUtility.ToSeedFileName(format));
    }

    /// <summary>
    ///     A single-language format names its language in the file name, so a seed cannot be written
    ///     without one - that is what left <c>MasterTextFile.properties</c> holding a language nothing
    ///     could identify.
    /// </summary>
    [Theory]
    [InlineData("nls", "ENGLISH", "mastertextfile_english.properties")]
    [InlineData("nls", "GERMAN", "mastertextfile_german.properties")]
    public void ToSeedFileName_SingleLanguageFormat_CarriesTheLanguageLowercased(
        string format, string language, string expected)
    {
        Assert.Equal(expected, LocalisationFormatUtility.ToSeedFileName(format, language));
    }

    [Fact]
    public void ToSeedFileName_SingleLanguageFormatWithNoLanguage_IsNull()
    {
        Assert.Null(LocalisationFormatUtility.ToSeedFileName("nls"));
    }
}
