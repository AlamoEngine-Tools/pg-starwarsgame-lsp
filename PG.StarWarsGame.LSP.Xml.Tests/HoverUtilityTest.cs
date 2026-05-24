// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
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

    // ── BuildReferenceHover ───────────────────────────────────────────────────

    private static GameObjectTypeDefinition MakeType(string typeName, string descEn = "A type.")
    {
        return new GameObjectTypeDefinition
        {
            TypeName = typeName,
            Description = new Dictionary<string, string> { ["en"] = descEn }
        };
    }

    private static GameReference MakeRef(string id, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, null, "file:///test.xml", line, col, len);
    }

    [Fact]
    public void BuildReferenceHover_ContainsTypeNameAndSymbolId()
    {
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("SpaceUnit"), "Fighter_Mk2", MakeRef("Fighter_Mk2", 2, 14, 11), "en");
        var md = hover.Contents.MarkupContent!.Value;
        Assert.Contains("SpaceUnit", md);
        Assert.Contains("Fighter_Mk2", md);
    }

    [Fact]
    public void BuildReferenceHover_ContainsTypeDescription()
    {
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("Faction", "A playable faction."), "EMPIRE", MakeRef("EMPIRE", 0, 13, 6), "en");
        Assert.Contains("A playable faction.", hover.Contents.MarkupContent!.Value);
    }

    [Fact]
    public void BuildReferenceHover_RangeCoversReferenceToken()
    {
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("SpaceUnit"), "Fighter_Mk2", MakeRef("Fighter_Mk2", 2, 14, 11), "en");
        Assert.Equal(2, hover.Range!.Start.Line);
        Assert.Equal(14, hover.Range.Start.Character);
        Assert.Equal(2, hover.Range.End.Line);
        Assert.Equal(25, hover.Range.End.Character); // 14 + 11
    }

    [Fact]
    public void BuildReferenceHover_IsMarkdown()
    {
        var hover = HoverUtility.BuildReferenceHover(
            MakeType("SpaceUnit"), "X_Wing", MakeRef("X_Wing", 0, 0, 6), "en");
        Assert.Equal(OmniSharp.Extensions.LanguageServer.Protocol.Models.MarkupKind.Markdown,
            hover.Contents.MarkupContent!.Kind);
    }
}