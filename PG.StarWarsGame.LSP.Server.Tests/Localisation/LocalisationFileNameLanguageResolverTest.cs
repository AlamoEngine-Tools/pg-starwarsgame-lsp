// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     The single place a single-language file's language is decided.
///     <para>
///         Formerly DAT-only, which left <c>.properties</c> with no language at all: every read path
///         hardcoded the service default and the one write path used the configured locale, so the
///         grid and the index disagreed about what language a file held.
///     </para>
/// </summary>
public sealed class LocalisationFileNameLanguageResolverTest
{
    private static ILanguageService LangService()
    {
        var services = new ServiceCollection();
        services.SupportLocalisationBaseline();
        return services.BuildServiceProvider().GetRequiredService<ILanguageService>();
    }

    // ── which formats carry a language in the name ───────────────────────────

    [Theory]
    [InlineData(".dat", true)]
    [InlineData(".properties", true)]
    [InlineData(".csv", false)]
    [InlineData(".xml", false)]
    public void CarriesLanguageInFileName_MatchesTheSingleLanguageFormats(string extension, bool expected)
    {
        Assert.Equal(expected, LocalisationFileNameLanguageResolver.CarriesLanguageInFileName(extension));
    }

    [Fact]
    public void CarriesLanguageInFileName_IsCaseInsensitive()
    {
        Assert.True(LocalisationFileNameLanguageResolver.CarriesLanguageInFileName(".PROPERTIES"));
    }

    // ── suffix resolution ────────────────────────────────────────────────────

    [Theory]
    [InlineData("/t/mastertextfile_english.dat", "ENGLISH")]
    [InlineData("/t/MasterTextFile_GERMAN.dat", "GERMAN")]
    [InlineData("/t/mastertextfile_german.properties", "GERMAN")]
    [InlineData("/t/MasterTextFile_French.properties", "FRENCH")]
    [InlineData("/t/creditstext_english.dat", "ENGLISH")]
    public void TryResolve_SuffixedName_ResolvesRegardlessOfCase(string path, string expected)
    {
        Assert.True(LocalisationFileNameLanguageResolver.TryResolve(path, LangService(), out var language));
        Assert.Equal(expected, language!.LanguageIdentifier);
    }

    [Theory]
    [InlineData("/t/mastertextfile.properties")]
    [InlineData("/t/mastertextfile.dat")]
    [InlineData("/t/mastertextfile_klingon.dat")]
    [InlineData("/t/mastertextfile_.dat")]
    public void TryResolve_NoResolvableSuffix_ReturnsFalse(string path)
    {
        Assert.False(LocalisationFileNameLanguageResolver.TryResolve(path, LangService(), out _));
    }

    // ── resolution with a fallback ───────────────────────────────────────────

    [Fact]
    public void Resolve_SuffixedName_PrefersTheNameOverTheFallback()
    {
        var langService = LangService();
        langService.TryGetByIdentifier("ITALIAN", out var fallback);

        var language = LocalisationFileNameLanguageResolver.Resolve(
            "/t/mastertextfile_german.properties", langService, fallback!, out var fromName);

        Assert.Equal("GERMAN", language.LanguageIdentifier);
        Assert.True(fromName);
    }

    /// <summary>
    ///     A file predating the naming convention still loads - refusing it would break every existing
    ///     NLS project on upgrade - but the caller is told so it can say which language it assumed.
    /// </summary>
    [Fact]
    public void Resolve_UnsuffixedName_FallsBackAndReportsThatItDidNotComeFromTheName()
    {
        var langService = LangService();
        langService.TryGetByIdentifier("ITALIAN", out var fallback);

        var language = LocalisationFileNameLanguageResolver.Resolve(
            "/t/mastertextfile.properties", langService, fallback!, out var fromName);

        Assert.Equal("ITALIAN", language.LanguageIdentifier);
        Assert.False(fromName);
    }

    // ── the configured game language ─────────────────────────────────────────

    [Fact]
    public void Configured_KnownIdentifier_ResolvesIt()
    {
        var language = LocalisationFileNameLanguageResolver.Configured(
            LangService(), new Core.Configuration.LocalisationConfig { Language = "GERMAN" });

        Assert.Equal("GERMAN", language.LanguageIdentifier);
    }

    [Fact]
    public void Configured_UnresolvableIdentifier_FallsBackToTheServiceDefault()
    {
        var langService = LangService();

        var language = LocalisationFileNameLanguageResolver.Configured(
            langService, new Core.Configuration.LocalisationConfig { Language = "KLINGON" });

        Assert.Equal(langService.Default.LanguageIdentifier, language.LanguageIdentifier);
    }

    /// <summary>
    ///     Guard for the category error this replaced: an ISO 639-1 locale is not an Alamo identifier,
    ///     so a locale reaching this path silently produced the default for every workspace.
    /// </summary>
    [Fact]
    public void Configured_IsoLocaleCode_DoesNotResolveAsALanguage()
    {
        var langService = LangService();

        var language = LocalisationFileNameLanguageResolver.Configured(
            langService, new Core.Configuration.LocalisationConfig { Language = "de" });

        Assert.Equal(langService.Default.LanguageIdentifier, language.LanguageIdentifier);
    }
}
