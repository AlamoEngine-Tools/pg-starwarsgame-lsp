// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.HoverStrategies;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Hover;

/// <summary>
///     Hover on one item of a slotted tuple value says which slot it fills and what that slot is.
///     Before slots the whole tag hovered as "MusicEventName, weight pairs", and no single item
///     hovered at all.
/// </summary>
public sealed class TupleSlotHoverStrategyTest
{
    private const string Xml = "<Root>\n<Pairs>Temperate, EI_TROOPER.ALO</Pairs>\n</Root>";

    private static ISchemaProvider Schema()
    {
        return new OneTagSchema(new XmlTagDefinition
        {
            Tag = "Pairs", ValueType = XmlValueType.TupleList,
            Slots =
            [
                new TupleSlotDefinition
                {
                    Label = "Terrain", ReferenceKind = ReferenceKind.Enum,
                    Enum = new EnumDefinition
                    {
                        Name = "TerrainType", Kind = EnumKind.SchemaFixed,
                        Values =
                        [
                            new EnumValueDefinition
                            {
                                Name = "TEMPERATE",
                                Description = new Dictionary<string, string> { ["en"] = "Grassland." }
                            }
                        ]
                    }
                },
                new TupleSlotDefinition { Label = "Model", ReferenceKind = ReferenceKind.ModelFile }
            ]
        });
    }

    private static string? HoverAt(int character)
    {
        return HoverAt(Xml, Schema(), character);
    }

    private static string? HoverAt(string xml, ISchemaProvider schema, int character)
    {
        var doc = XmlUtility.CreateHtmlDocument(xml);
        XmlUtility.TryGetRootNode(doc, out var root);
        XmlUtility.TryFindNode(doc, 1, out var node);
        var index = GameIndex.Empty with
        {
            AssetFiles = MergedAssetFileIndex.Merge([],
                ["data/art/models/ei_trooper.alo", "data/art/textures/i_unit.tga"])
        };
        var ctx = new HoverContext("file:///units.xml", index, schema, doc, root!,
            node ?? new HtmlDocument().DocumentNode, false, 1, character, "en");

        return new TupleSlotHoverStrategy().Handle(ctx)?.Contents.MarkupContent?.Value;
    }

    [Fact]
    public void OnAListMapKey_MidList_IsTheKeyHover()
    {
        // The engine reads "Idle" as a key wherever it sits, so the hover must too.
        const string xml = "<Root>\n<Map>Attention, A, Idle, B</Map>\n</Root>";
        var schema = new OneTagSchema(new XmlTagDefinition
        {
            Tag = "Map", ValueType = XmlValueType.ListMap,
            Slots =
            [
                new TupleSlotDefinition
                {
                    Label = "Animation", ReferenceKind = ReferenceKind.Enum,
                    Enum = new EnumDefinition
                    {
                        Name = "AnimationType", Kind = EnumKind.SchemaFixed,
                        Values =
                        [
                            new EnumValueDefinition { Name = "Attention" }, new EnumValueDefinition { Name = "Idle" }
                        ]
                    }
                },
                new TupleSlotDefinition { Label = "Unit", ReferenceKind = ReferenceKind.XmlObject }
            ]
        });

        var hover = HoverAt(xml, schema, xml.Split('\n')[1].IndexOf("Idle", StringComparison.Ordinal) + 1);

        Assert.NotNull(hover);
        Assert.Contains("**Animation** - `AnimationType`", hover);
    }

    [Fact]
    public void OnATextureItem_IsTheTextureHover()
    {
        const string xml = "<Root>\n<Icons>Space, i_unit.tga</Icons>\n</Root>";
        var schema = new OneTagSchema(new XmlTagDefinition
        {
            Tag = "Icons", ValueType = XmlValueType.TupleList,
            Slots =
            [
                new TupleSlotDefinition { Label = "Context" },
                new TupleSlotDefinition { Label = "Icon", ReferenceKind = ReferenceKind.TextureFile }
            ]
        });

        var hover = HoverAt(xml, schema, xml.Split('\n')[1].IndexOf("i_unit", StringComparison.Ordinal) + 1);

        Assert.NotNull(hover);
        Assert.Contains("i_unit.tga", hover, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnAnEnumItem_NamesTheSlot_TheEnum_AndTheValue()
    {
        // "<Pairs>" is 7 characters, so "Temperate" starts at column 7.
        var hover = HoverAt(9);

        Assert.NotNull(hover);
        Assert.Contains("Terrain", hover);
        Assert.Contains("TerrainType", hover);
        Assert.Contains("Grassland.", hover);
    }

    [Fact]
    public void OnAModelItem_IsTheModelHover()
    {
        var hover = HoverAt(Xml.Split('\n')[1].IndexOf("EI_TROOPER", StringComparison.Ordinal) + 2);

        Assert.NotNull(hover);
        Assert.Contains("EI_TROOPER.ALO", hover, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BetweenItems_SaysNothing()
    {
        Assert.Null(HoverAt(Xml.Split('\n')[1].IndexOf(',', StringComparison.Ordinal)));
    }

    private sealed class OneTagSchema(XmlTagDefinition tag) : ISchemaProvider
    {
        public XmlTagDefinition? GetTag(string tagName)
        {
            return tag.Tag.Equals(tagName, StringComparison.OrdinalIgnoreCase) ? tag : null;
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

        public IReadOnlyList<XmlTagDefinition> AllTags => [tag];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}