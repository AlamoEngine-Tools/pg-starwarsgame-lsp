// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class AbilitySfxMapHandlerTest
{
    private static readonly AbilitySfxMapHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Ability_SFX", XmlValueType.AbilitySfxMap);

    [Theory]
    [InlineData("ABILITY_MOVE, SFX_Move")]
    [InlineData("ABILITY_ATTACK,SFX_Attack")]
    [InlineData("ABILITY_MOVE,")]
    public void Valid_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABILITY_MOVE")]
    [InlineData(",SFX_Move")]
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

    // ── AbilityType first-field validation ──────────────────────────────────

    private static DiagnosticsContext CtxWithAbilityTypeSet(params string[] names)
    {
        var set = new HardcodedReferenceSet
        {
            Name = "AbilityType",
            Values = names.Select(n => new HardcodedReferenceSetValue { Name = n }).ToList()
        };
        return new DiagnosticsContext(new StubSchemaProvider([set]), GameIndex.Empty, "file:///test.xml", "en");
    }

    [Theory]
    [InlineData("HUNT, SFX_Hunt")]
    [InlineData("hunt, SFX_Hunt")]
    [InlineData("DEFEND,")]
    public void AbilityCode_KnownValue_WithAbilityTypeSet_ReturnsNoDiagnostics(string value)
    {
        var ctx = CtxWithAbilityTypeSet("HUNT", "DEFEND");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void AbilityCode_UnknownValue_WithAbilityTypeSet_ReturnsError()
    {
        var ctx = CtxWithAbilityTypeSet("HUNT", "DEFEND");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "UNKNOWN_CODE, SFX_Event"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void AbilityCode_UnknownValue_WithoutAbilityTypeSet_ReturnsNoDiagnostics()
    {
        // Graceful degradation: no extra error when AbilityType set is absent
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "UNKNOWN_CODE, SFX_Event"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
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
        var ctx = new DiagnosticsContext(new StubSchemaProvider([]), IndexWithSfxEvent("SFX_Hunt"), "file:///test.xml",
            "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "HUNT, SFX_Hunt"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void SfxEvent_UnresolvedSymbol_WithLoadedIndex_ReturnsError()
    {
        var ctx = new DiagnosticsContext(new StubSchemaProvider([]), IndexWithSfxEvent("Other_SFX"), "file:///test.xml",
            "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "HUNT, Missing_SFX"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Missing_SFX", d.Message);
    }

    [Fact]
    public void SfxEvent_UnresolvedSymbol_WithEmptyIndex_ReturnsNoDiagnostics()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "HUNT, Missing_SFX"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void SfxEvent_EmptyName_IsAllowed()
    {
        var ctx = new DiagnosticsContext(new StubSchemaProvider([]), IndexWithSfxEvent("Some_SFX"), "file:///test.xml",
            "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "HUNT,"), ctx).ToList();
        Assert.Empty(results);
    }

    private sealed class StubSchemaProvider(IReadOnlyList<HardcodedReferenceSet> sets) : ISchemaProvider
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

        public EnumDefinition? GetEnum(string _)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => sets;
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}