// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class HardPointSfxMapHandlerTest
{
    private static readonly HardPointSfxMapHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("HardPoint_SFX", XmlValueType.HardPointSfxMap);

    [Theory]
    [InlineData("HARDPOINT_WEAPON, SFX_Fire")]
    [InlineData("HARDPOINT_ENGINE,SFX_Engine")]
    [InlineData("HARDPOINT_WEAPON,")]
    public void Valid_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HARDPOINT_WEAPON")]
    [InlineData(",SFX_Fire")]
    public void Invalid_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    // ── HardPointType first-field validation ────────────────────────────────

    private static DiagnosticsContext CtxWithHardPointTypeEnum(params string[] names)
    {
        var enumDef = new EnumDefinition
        {
            Name = "HardPointType", Kind = EnumKind.SchemaFixed,
            Values = names.Select(n => new EnumValueDefinition { Name = n }).ToList()
        };
        return new DiagnosticsContext(new StubSchemaWithEnum(enumDef), GameIndex.Empty, "file:///test.xml", "en");
    }

    [Theory]
    [InlineData("HARD_POINT_WEAPON_LASER, SFX_Fire")]
    [InlineData("hard_point_weapon_laser,")]
    public void HardPointType_KnownValue_WithEnum_ReturnsNoDiagnostics(string value)
    {
        var ctx = CtxWithHardPointTypeEnum("HARD_POINT_WEAPON_LASER", "HARD_POINT_WEAPON_MISSILE");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void HardPointType_UnknownValue_WithEnum_ReturnsError()
    {
        var ctx = CtxWithHardPointTypeEnum("HARD_POINT_WEAPON_LASER");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "INVALID_TYPE, SFX_Fire"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("INVALID_TYPE", d.Message);
    }

    [Fact]
    public void HardPointType_UnknownValue_WithoutEnum_ReturnsNoDiagnostics()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "INVALID_TYPE, SFX_Fire"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void HardPointType_UnknownValue_HighlightsOnlyTheTypeToken()
    {
        // A broken token must highlight only itself, not the whole tuple value
        // (fact position is line 0, column 0 via MakeFact).
        var ctx = CtxWithHardPointTypeEnum("HARD_POINT_WEAPON_LASER");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "INVALID_TYPE, SFX_Fire"), ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(0, d.OverrideLine);
        Assert.Equal(0, d.OverrideColumn);
        Assert.Equal("INVALID_TYPE".Length, d.OverrideLength);
    }

    [Fact]
    public void SfxEvent_UnresolvedSymbol_HighlightsOnlyTheSfxToken()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithSfxEvent("Other_SFX"),
            "file:///test.xml", "en");
        var results = Sut.Handle(
            XmlHandlerTestFixtures.MakeFact(Tag, "HARDPOINT_WEAPON, Missing_SFX"), ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(0, d.OverrideLine);
        Assert.Equal(18, d.OverrideColumn); // offset of "Missing_SFX" in the raw value
        Assert.Equal("Missing_SFX".Length, d.OverrideLength);
    }

    // ── SFX event second-field validation ────────────────────────────────────

    private static GameIndex IndexWithSfxEvent(string sfxEventId)
    {
        var symbol = new GameSymbol(sfxEventId, GameSymbolKind.Asset, "SFXEvent", new UnknownOrigin("test"), null);
        var defs = ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase)
            .Add(sfxEventId, ImmutableArray.Create(symbol));
        return new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    [Fact]
    public void SfxEvent_ResolvedSymbol_ReturnsNoDiagnostics()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithSfxEvent("Unit_HP_LASER"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "HARD_POINT_WEAPON_LASER, Unit_HP_LASER"), ctx)
            .ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void SfxEvent_UnresolvedSymbol_WithLoadedIndex_ReturnsError()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithSfxEvent("Other_SFX"), "file:///test.xml",
            "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "HARD_POINT_WEAPON_LASER, Missing_SFX"), ctx)
            .ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Missing_SFX", d.Message);
    }

    [Fact]
    public void SfxEvent_UnresolvedSymbol_WithEmptyIndex_ReturnsNoDiagnostics()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "HARD_POINT_WEAPON_LASER, Missing_SFX"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void SfxEvent_EmptyName_IsAllowed()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithSfxEvent("Some_SFX"), "file:///test.xml",
            "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "HARD_POINT_WEAPON_LASER,"), ctx).ToList();
        Assert.Empty(results);
    }

    private sealed class StubSchemaWithEnum(EnumDefinition enumDef) : ISchemaProvider
    {
        public XmlTagDefinition? GetTag(string _)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string _)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string name)
        {
            return name.Equals(enumDef.Name, StringComparison.OrdinalIgnoreCase) ? enumDef : null;
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [enumDef];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}