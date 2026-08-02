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
}
