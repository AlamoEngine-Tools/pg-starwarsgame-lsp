// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     <c>Autoresolve_Exclusion_Locations</c> is repeated (planet, mode) pairs flattened into one
///     comma-separated list, e.g. <c>Fondor, land, Geonosis, space</c>. The planets are indexed as
///     object references by the parser; this handler owns the mode slots and the pairing itself.
/// </summary>
public sealed class PlanetModeExclusionListHandlerTest
{
    private static readonly PlanetModeExclusionListHandler Sut = new();

    // Canonical engine type + schema refinement: XmlValueType mirrors the game's own type table and
    // has no entry for this, so the alternating-slot meaning rides on SemanticType.
    private static readonly XmlTagDefinition Tag = XmlHandlerTestFixtures.MakeTag(
        "Autoresolve_Exclusion_Locations", XmlValueType.TypeReferenceList,
        TagSemanticType.PlanetModePairList);

    private static DiagnosticsContext CtxWithModes(params string[] modes)
    {
        var schema = new StubSchemaWithModeEnum(new EnumDefinition
        {
            Name = "StoryBattleMode", Kind = EnumKind.DynamicXml, Values = []
        });
        var index = GameIndex.Empty with
        {
            WorkspaceDynamicEnumValues = ImmutableDictionary
                .Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase)
                .Add("StoryBattleMode", [.. modes])
        };
        return new DiagnosticsContext(schema, index, "file:///test.xml", "en");
    }

    [Theory]
    [InlineData("Fondor, land")]
    [InlineData("Fondor, land, Geonosis, space")]
    [InlineData("Fondor, LAND, Geonosis, Space")]
    public void WellFormedPairs_ReturnNoDiagnostics(string value)
    {
        var ctx = CtxWithModes("Land", "Space", "Ground", "Either");
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), ctx));
    }

    [Fact]
    public void UnknownMode_ReturnsErrorAtTheModeToken()
    {
        var ctx = CtxWithModes("Land", "Space");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Fondor, orbit"), ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("orbit", d.Message);
        // Must underline only the bad mode, not the whole list.
        Assert.Equal("Fondor, ".Length, d.OverrideColumn);
        Assert.Equal("orbit".Length, d.OverrideLength);
    }

    [Fact]
    public void UnknownModeInLaterPair_IsFoundAndPositioned()
    {
        // Slot arithmetic has to survive past the first pair.
        var ctx = CtxWithModes("Land", "Space");
        var results = Sut.Handle(
            XmlHandlerTestFixtures.MakeFact(Tag, "Fondor, land, Geonosis, orbit"), ctx).ToList();

        var d = Assert.Single(results);
        Assert.Contains("orbit", d.Message);
        Assert.Equal("Fondor, land, Geonosis, ".Length, d.OverrideColumn);
    }

    [Fact]
    public void PlanetWithoutMode_ReturnsErrorAtThatPlanet()
    {
        // An odd token count throws every following pair out of step, so the trailing planet is the
        // useful place to point rather than the value as a whole.
        var ctx = CtxWithModes("Land", "Space");
        var results = Sut.Handle(
            XmlHandlerTestFixtures.MakeFact(Tag, "Fondor, land, Geonosis"), ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Geonosis", d.Message);
        Assert.Equal("Fondor, land, ".Length, d.OverrideColumn);
        Assert.Equal("Geonosis".Length, d.OverrideLength);
    }

    [Fact]
    public void PlanetNamesAreNotValidatedHere()
    {
        // The parser emits them as object references, so the unresolved-reference pipeline owns
        // them; duplicating that here would double-report every bad planet.
        var ctx = CtxWithModes("Land", "Space");
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Not_A_Planet, land"), ctx));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyValue_ReturnsNoDiagnostics(string value)
    {
        var ctx = CtxWithModes("Land", "Space");
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), ctx));
    }

    [Fact]
    public void NoModeEnumAvailable_ReturnsNoModeDiagnostics()
    {
        // With no value set to check against, silence beats guessing - but a broken pair is still
        // structurally wrong and stays reported.
        var ctx = CtxWithModes();
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Fondor, whatever"), ctx));
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, "Fondor, orbit"),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void PlainTypeReferenceList_WithoutTheRefinement_IsLeftAlone()
    {
        // TypeReferenceList is a shared canonical type used by many tags; only the ones the schema
        // marks as pair lists have alternating slots, and the rest must not be validated as such.
        var plainList = XmlHandlerTestFixtures.MakeTag("Allies", XmlValueType.TypeReferenceList);
        var ctx = CtxWithModes("Land", "Space");

        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(plainList, "Empire, Pirates, Hostile"), ctx));
    }

    private sealed class StubSchemaWithModeEnum(EnumDefinition enumDef) : ISchemaProvider
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

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [enumDef];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public EnumDefinition? GetEnum(string enumName)
        {
            return string.Equals(enumName, enumDef.Name, StringComparison.OrdinalIgnoreCase)
                ? enumDef
                : null;
        }

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}
