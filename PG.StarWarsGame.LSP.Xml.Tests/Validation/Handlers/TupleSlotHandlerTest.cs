// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The per-item checks of a slotted tuple value: an enum item against its enum, a model item
///     against the asset catalog. The same checks a whole tag of that kind gets, item by item.
/// </summary>
public sealed class TupleSlotHandlerTest
{
    private static readonly XmlTagDefinition Tag = new()
    {
        Tag = "Land_Terrain_Model_Mapping", ValueType = XmlValueType.TupleList
    };

    private static readonly TupleSlotDefinition Terrain = new()
    {
        Label = "Terrain", ReferenceKind = ReferenceKind.Enum,
        Enum = new EnumDefinition
        {
            Name = "TerrainType", Kind = EnumKind.SchemaFixed,
            Values = [new EnumValueDefinition { Name = "TEMPERATE" }, new EnumValueDefinition { Name = "URBAN" }]
        }
    };

    private static readonly TupleSlotDefinition Model = new()
        { Label = "Model", ReferenceKind = ReferenceKind.ModelFile };

    private static XmlTupleSlotFact Fact(TupleSlotDefinition slot, string value)
    {
        return new XmlTupleSlotFact("file:///units.xml", 4, 8, value.Length, Tag, slot, value);
    }

    private static DiagnosticsContext WithModels(params string[] paths)
    {
        var index = GameIndex.Empty with { AssetFiles = MergedAssetFileIndex.Merge([], paths) };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///units.xml", "en");
    }

    // ── enum items ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("TEMPERATE")]
    [InlineData("Temperate")]
    public void Enum_AKnownValue_InAnyCase_IsFine(string value)
    {
        Assert.Empty(new TupleSlotEnumHandler().Handle(Fact(Terrain, value), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Enum_AnUnknownValue_NamesTheSlotAndTheEnum()
    {
        var d = Assert.Single(
            new TupleSlotEnumHandler().Handle(Fact(Terrain, "Moon"), XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("'Moon'", d.Message);
        Assert.Contains("TerrainType", d.Message);
        Assert.Contains("Terrain", d.Message);
    }

    [Fact]
    public void Enum_IgnoresAModelSlot()
    {
        Assert.Empty(new TupleSlotEnumHandler().Handle(Fact(Model, "Moon"), XmlHandlerTestFixtures.EmptyCtx));
    }

    // ── model items ──────────────────────────────────────────────────────────

    [Fact]
    public void Model_AFileInTheCatalog_IsFine()
    {
        Assert.Empty(new TupleSlotAssetHandler().Handle(Fact(Model, "EI_TROOPER.ALO"),
            WithModels("data/art/models/ei_trooper.alo")));
    }

    [Fact]
    public void Model_AMissingFile_IsAWarning_LikeAWholeModelTag()
    {
        var d = Assert.Single(new TupleSlotAssetHandler().Handle(Fact(Model, "EI_GONE.ALO"),
            WithModels("data/art/models/ei_trooper.alo")));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("'EI_GONE.ALO'", d.Message);
    }

    [Fact]
    public void Model_NotAnAlo_IsAnError_UnderTheFormatId()
    {
        var d = Assert.Single(new TupleSlotAssetHandler().Handle(Fact(Model, "EI_TROOPER.TGA"),
            WithModels("data/art/models/ei_trooper.alo")));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal(DiagnosticIds.TupleSlotModelFileFormat, d.Id);
    }

    // ── other asset items: the rules a whole tag of that kind gets ────────────

    private static readonly TupleSlotDefinition Icon = new()
        { Label = "Icon", ReferenceKind = ReferenceKind.TextureFile };

    private static readonly TupleSlotDefinition Map = new() { Label = "Map", ReferenceKind = ReferenceKind.MapFile };

    [Fact]
    public void Texture_IsSatisfiedByTheOtherFormat_AsAWholeTextureTagIs()
    {
        Assert.Empty(new TupleSlotAssetHandler().Handle(Fact(Icon, "i_unit.tga"),
            WithModels("data/art/textures/i_unit.dds")));
    }

    [Fact]
    public void Texture_AMissingFile_IsAWarning_UnderTheAssetId()
    {
        var d = Assert.Single(new TupleSlotAssetHandler().Handle(Fact(Icon, "i_gone.tga"), WithModels()));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.TupleSlotAssetFileExistence, d.Id);
        Assert.StartsWith("Texture file 'i_gone.tga' was not found", d.Message);
    }

    [Fact]
    public void Map_AMissingFile_IsAnError_AsAWholeMapTagIs()
    {
        var d = Assert.Single(new TupleSlotAssetHandler().Handle(Fact(Map, "_gone.ted"), WithModels()));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Model_IgnoresAnEnumSlot()
    {
        Assert.Empty(new TupleSlotAssetHandler().Handle(Fact(Terrain, "Temperate"), WithModels()));
    }
}