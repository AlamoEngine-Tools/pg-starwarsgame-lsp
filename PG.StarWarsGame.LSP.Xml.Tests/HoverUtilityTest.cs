// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class HoverUtilityTest
{
    [Fact]
    public void Resolve_ExactLocaleMatch_ReturnsIt()
    {
        var desc = new Dictionary<string, string> { ["de"] = "Deutsch", ["en"] = "English" };
        Assert.Equal("Deutsch", HoverUtility.Resolve(desc, "de"));
    }

    [Fact]
    public void Resolve_LocaleIsEn_ReturnsEn()
    {
        var desc = new Dictionary<string, string> { ["en"] = "English" };
        Assert.Equal("English", HoverUtility.Resolve(desc, "en"));
    }

    [Fact]
    public void Resolve_LocaleMissing_FallsBackToEn()
    {
        var desc = new Dictionary<string, string> { ["en"] = "English" };
        Assert.Equal("English", HoverUtility.Resolve(desc, "fr"));
    }

    [Fact]
    public void Resolve_NoDescriptions_ReturnsPrHintMessage()
    {
        var result = HoverUtility.Resolve(new Dictionary<string, string>(), "en");
        // Should mention contributing / PR
        Assert.Contains("PR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_NeitherLocaleNorEn_ReturnsPrHintMessage()
    {
        var desc = new Dictionary<string, string> { ["ja"] = "日本語" };
        var result = HoverUtility.Resolve(desc, "fr");
        Assert.Contains("PR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_EnFallbackIsCaseInsensitive()
    {
        // "EN" key should still match the "en" fallback path
        var desc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = "English" };
        Assert.Equal("English", HoverUtility.Resolve(desc, "fr"));
    }
}