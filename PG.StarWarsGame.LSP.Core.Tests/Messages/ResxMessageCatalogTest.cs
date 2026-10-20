// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Resources;
using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Core.Tests.Messages;

public sealed class ResxMessageCatalogTest
{
    private static readonly ResourceManager Resources = new(
        "PG.StarWarsGame.LSP.Core.Tests.Messages.TestMessages", typeof(ResxMessageCatalogTest).Assembly);

    [Fact]
    public void FormatsTheMessageForTheLocale()
    {
        Assert.Equal("Schaden 0.5 bei 'x'", new ResxMessageCatalog(Resources, "de").Format("Damage", 0.5, "x"));
    }

    /// <summary>
    ///     A value the author wrote stays as written: a German message still shows 0.5, the way the
    ///     XML holds it, never 0,5.
    /// </summary>
    [Fact]
    public void FormatsValuesInvariantWhateverTheLocale()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Assert.Equal("Damage 0.5 at 'x'", new ResxMessageCatalog(Resources, "en").Format("Damage", 0.5, "x"));
            Assert.Equal("Schaden 0.5 bei 'x'", new ResxMessageCatalog(Resources, "de").Format("Damage", 0.5, "x"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FallsBackToEnglishWhereTheLocaleHasNoTranslation()
    {
        Assert.Equal("Takes {x} literally", new ResxMessageCatalog(Resources, "de").Format("Braces"));
        Assert.Equal("Damage 1 at 'y'", new ResxMessageCatalog(Resources, "fr").Format("Damage", 1, "y"));
    }

    [Fact]
    public void FallsBackToEnglishForALocaleNoCultureKnows()
    {
        Assert.Equal("Damage 1 at 'y'", new ResxMessageCatalog(Resources, "not-a-locale").Format("Damage", 1, "y"));
    }

    /// <summary>A missing key is a bug in the code, not a message to show: it fails loudly.</summary>
    [Fact]
    public void RefusesAKeyTheCatalogueDoesNotCarry()
    {
        var error = Assert.Throws<KeyNotFoundException>(() =>
            new ResxMessageCatalog(Resources, "en").Format("Missing"));
        Assert.Contains("Missing", error.Message);
    }
}