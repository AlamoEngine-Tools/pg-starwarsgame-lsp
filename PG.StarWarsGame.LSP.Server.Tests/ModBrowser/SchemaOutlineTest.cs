// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.ModBrowser;

namespace PG.StarWarsGame.LSP.Server.Tests.ModBrowser;

/// <summary>
///     The Schema outline (#270): the XML types, each opening into its tags; a tag's page is its
///     documentation. Then the enums, and the behaviours with what decides each. All of it is the
///     schema's own, read as the hovers read it.
/// </summary>
public sealed class SchemaOutlineTest
{
    private const string Units = "file:///c:/work/mod/data/xml/units.xml";

    private static Dictionary<string, string> En(string text)
    {
        return new Dictionary<string, string> { ["en"] = text };
    }

    // Dynamic: its values are the game's and the project's, from their enum files; the schema only
    // describes some.
    private static readonly EnumDefinition s_damage = new()
    {
        Name = "DamageType",
        Kind = EnumKind.DynamicXml,
        Values =
        [
            new EnumValueDefinition { Name = "Damage_Laser", Description = En("Lasers") },
            new EnumValueDefinition { Name = "Damage_Ion" }
        ]
    };

    // Built into the engine: the schema's values are all there are.
    private static readonly EnumDefinition s_weather = new()
    {
        Name = "WeatherType",
        Kind = EnumKind.SchemaFixed,
        Values =
        [
            new EnumValueDefinition { Name = "Rain", Value = "WEATHER_RAIN", Description = En("Rain falls") },
            new EnumValueDefinition { Name = "Snow" }
        ]
    };

    private static readonly ObjectKindDefinition s_spaceUnit = new()
    {
        Kind = "SpaceUnit", Behaviors = ["SPACE_UNIT"], Flags = ["Is_Space"], Description = En("Flies in space")
    };

    private sealed class Schema : NullSchemaProvider
    {
        public override IReadOnlyList<ObjectKindDefinition> AllKinds => [s_spaceUnit];

        public override IReadOnlyList<EnumDefinition> AllEnums => [s_damage, s_weather];

        public override IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes =>
        [
            new() { TypeName = "GameObjectType", NameTag = "Name", Description = En("Units and structures") },
            new() { TypeName = "SFXEvent" }
        ];

        public override IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return typeName != "GameObjectType"
                ? []
                :
                [
                    new XmlTagDefinition
                    {
                        Tag = "Tactical_Health", ValueType = XmlValueType.Float, Description = En("Hit points"),
                        Notes = [new SchemaNote(SchemaNoteKind.Deprecated, En("Use Shield_Points"))]
                    },
                    new XmlTagDefinition
                    {
                        Tag = "Affiliation", ValueType = XmlValueType.NameReferenceList,
                        ReferenceKind = ReferenceKind.XmlObject, ReferenceTypeName = "Faction",
                        MultipleAllowed = true
                    },
                    new XmlTagDefinition
                    {
                        Tag = "Damage_Type", ValueType = XmlValueType.DynamicEnumValue,
                        ReferenceKind = ReferenceKind.Enum, Enum = s_damage
                    }
                ];
        }
    }

    private static (SymbolCatalog Catalog, SymbolRelations Relations, SymbolPreviews Previews) Build()
    {
        // A space unit, by its behaviour and its flag both.
        var destroyer = new GameSymbol("Star_Destroyer", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin(Units, 1, 2), null, Behaviors: ["SPACE_UNIT"], Flags: ["Is_Space"]);
        var index = GameIndex.Empty with
        {
            // The game ships two damage types; the project adds one and repeats one.
            Baseline = BaselineIndex.Empty with
            {
                DynamicEnumValues = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                    .Add("DamageType", ["Damage_Laser", "Damage_Ion"])
            },
            WorkspaceDynamicEnumValues = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("DamageType", ["Damage_Ion", "Damage_Mod"]),
            // The Star Destroyer's definition uses the project's damage type, as the parser records it.
            WorkspaceReferences = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("enum:DamageType/Damage_Mod",
                    [new GameReference("enum:DamageType/Damage_Mod", null, null, Units, 3, 14, 10)]),
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Units, new DocumentIndex(Units, 1, [destroyer], [], LayerRank: 0, LayerName: "Mod")),
            WorkspaceDefinitions =
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(destroyer.Id, [destroyer])
        };
        var schema = new Schema();
        var catalog = SymbolCatalog.Build(index, [s_spaceUnit], [s_damage, s_weather], schema);
        var relations = new SymbolRelations(index, schema, new NoTags(), catalog);
        return (catalog, relations, new SymbolPreviews(index, relations, null!, null!, null!, schema: schema));
    }

    private sealed class NoTags : IVariantTagSource
    {
        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return null;
        }
    }

    [Fact]
    public void TheSchemaOutline_ListsTheTypes_TheEnums_AndTheBehaviours()
    {
        var (catalog, _, _) = Build();

        Assert.Equal(
            [
                new SymbolPageSummary("schemaTypes", 2), new SymbolPageSummary("enums", 2),
                new SymbolPageSummary("behaviours", 1)
            ],
            catalog.Outline(SymbolOutlines.Schema));
    }

    [Fact]
    public void AType_OpensIntoItsTags_EachByWhatItTakes()
    {
        var (_, relations, _) = Build();

        Assert.Equal(3,
            relations.Node("schemaTypes", "GameObjectType").Relations.Single(r => r.Relation == "tags").Count);
        var tags = relations.Rows("schemaTypes", "GameObjectType", "tags")!;
        Assert.Equal(
            [("Affiliation", "NameReferenceList"), ("Damage_Type", "DynamicEnumValue"), ("Tactical_Health", "Float")],
            tags.Select(t => (t.Name, t.Type)));
        Assert.Equal("schemaTags", tags[0].Page);
        Assert.Empty(relations.Rows("schemaTypes", "SFXEvent", "tags")!);
        Assert.Equal([("definition", "schema")],
            relations.Node("schemaTypes", "GameObjectType").Actions.Select(a => (a.Action, a.Reason)));
    }

    [Fact]
    public void ATag_IsItsDocumentation()
    {
        var (_, _, previews) = Build();

        var health = previews.Preview("schemaTags", "GameObjectType/Tactical_Health");
        Assert.Equal("schemaTag", health.Adapter);
        Assert.Equal([("identity", 0, 0, 2, 2), ("documentation", 2, 0, 4, 2)],
            health.Tiles.Select(t => (t.Kind, t.X, t.Y, t.Width, t.Height)));
        var docs = health.Tiles[1];
        Assert.Equal([new PreviewProperty("valueType", "Float"), new PreviewProperty("type", "GameObjectType")],
            docs.Properties);
        Assert.Equal("Hit points", docs.Text);
        Assert.Equal([new PreviewNote("Deprecated", "Use Shield_Points", null)], docs.Notes);

        var affiliation = previews.Preview("schemaTags", "GameObjectType/Affiliation").Tiles[1];
        Assert.Equal(
        [
            new PreviewProperty("valueType", "NameReferenceList"), new PreviewProperty("type", "GameObjectType"),
            new PreviewProperty("references", "Faction"), new PreviewProperty("repeats", "")
        ], affiliation.Properties);
    }

    // A tag that takes an enum shows its values beside its documentation.
    [Fact]
    public void ATagTakingAnEnum_ShowsItsValues()
    {
        var (_, _, previews) = Build();

        var damage = previews.Preview("schemaTags", "GameObjectType/Damage_Type");
        Assert.Equal([("identity", 0, 0, 2, 2), ("documentation", 2, 0, 2, 2), ("values", 4, 0, 2, 2)],
            damage.Tiles.Select(t => (t.Kind, t.X, t.Y, t.Width, t.Height)));
        Assert.Contains(new PreviewProperty("enum", "DamageType"), damage.Tiles[1].Properties!);
        // What it references is the enum, said once.
        Assert.DoesNotContain(damage.Tiles[1].Properties!, p => p.Key == "references");
        Assert.Equal([("Damage_Laser", "Lasers"), ("Damage_Ion", null), ("Damage_Mod", null)],
            damage.Tiles[2].Entries.Select(e => (e.Name, e.Detail)));
    }

    // An enum says whether it is built into the engine or read from the game's files, and lists its
    // values: a built-in one the schema's, a dynamic one the game's and the project's together.
    [Fact]
    public void AnEnum_SaysWhetherItIsBuiltIn_AndListsItsValues()
    {
        var (catalog, relations, previews) = Build();

        Assert.Equal([("DamageType", "dynamic"), ("WeatherType", "fixed")],
            catalog.Page(new GetSymbolPageParams("enums")).Rows.Select(r => (r.Name, r.Type)));
        Assert.Equal(new SymbolAction("open", null), relations.Node("enums", "DamageType").Open);

        var damage = previews.Preview("enums", "DamageType");
        Assert.Equal("enum", damage.Adapter);
        Assert.Equal(
        [
            ("identity", 0, 0, 2, 2), ("documentation", 2, 0, 2, 2), ("values", 4, 0, 2, 4),
            ("entries", 0, 2, 4, 2)
        ], damage.Tiles.Select(t => (t.Kind, t.X, t.Y, t.Width, t.Height)));
        Assert.Equal([new PreviewProperty("kind", "dynamic")], damage.Tiles[1].Properties);
        Assert.Equal(["Damage_Laser", "Damage_Ion", "Damage_Mod"], damage.Tiles[2].Entries.Select(e => e.Name));
        Assert.Equal("usedBy", damage.Tiles[3].Relation);
        // The tags typed with it are the tag pages they are, each on its type.
        Assert.Equal([("Damage_Type", "schemaTags", "GameObjectType/Damage_Type")],
            relations.Rows("enums", "DamageType", "usedBy")!.Select(r => (r.Name, r.Page, r.Id)));
        // One tag name is a tag on several types: its line says which.
        Assert.Equal([("Damage_Type", "GameObjectType")], damage.Tiles[3].Entries.Select(e => (e.Name, e.Detail)));

        var weather = previews.Preview("enums", "WeatherType");
        Assert.Equal([new PreviewProperty("kind", "fixed")], weather.Tiles[1].Properties);
        Assert.Equal([("Rain", "Rain falls"), ("Snow", null)],
            weather.Tiles[2].Entries.Select(e => (e.Name, e.Detail)));
        // A value apart from its name is said beside it; one that is its name is not said twice.
        Assert.Equal(["WEATHER_RAIN", null], weather.Tiles[2].Entries.Select(e => e.Value));
    }

    // An enum's values are its children in the rail and a list of their own in the table: each value
    // once, from the game or from the project.
    [Fact]
    public void AnEnum_OpensIntoItsValues()
    {
        var (_, relations, _) = Build();

        var node = relations.Node("enums", "DamageType");
        Assert.Equal(["values", "usedBy"], node.Relations.Select(r => r.Relation));
        Assert.Equal(3, node.Relations[0].Count);
        Assert.Equal(
        [
            ("Damage_Ion", "DamageType", "game"), ("Damage_Laser", "DamageType", "game"),
            ("Damage_Mod", "DamageType", "project")
        ], relations.Rows("enums", "DamageType", "values")!.Select(r => (r.Name, r.Type, r.Origin)));
        // Key and value, each a field of its own: the name XML writes and what the engine uses for it.
        Assert.Equal([("Rain", "WEATHER_RAIN"), ("Snow", "Snow")],
            relations.Rows("enums", "WeatherType", "values")!.Select(r => (r.Name, r.Value)));
        // A value is the schema's, with nothing of its own to preview; it opens into the objects using it.
        var value = relations.Node("enumValues", "DamageType/Damage_Mod");
        Assert.Equal(new SymbolAction("open", "noPreview"), value.Open);
        Assert.Equal([new SymbolRelation("usedBy", 1, null)], value.Relations);
        Assert.Equal(["Star_Destroyer"],
            relations.Rows("enumValues", "DamageType/Damage_Mod", "usedBy")!.Select(r => r.Name));
        Assert.Equal([new SymbolRelation("usedBy", 0, "none")],
            relations.Node("enumValues", "DamageType/Damage_Laser").Relations);
    }

    [Fact]
    public void ABehaviour_SaysWhatDecidesIt_AndListsTheObjectsThatHaveIt()
    {
        var (_, relations, previews) = Build();

        Assert.Equal(["Star_Destroyer"], relations.Rows("behaviours", "SpaceUnit", "objects")!.Select(r => r.Name));
        var preview = previews.Preview("behaviours", "SpaceUnit");
        Assert.Equal("behaviour", preview.Adapter);
        Assert.Equal([new PreviewProperty("behaviours", "SPACE_UNIT"), new PreviewProperty("flags", "Is_Space")],
            preview.Tiles.Single(t => t.Kind == "documentation").Properties);
        Assert.Equal("Flies in space", preview.Tiles.Single(t => t.Kind == "documentation").Text);
        Assert.Equal("objects", preview.Tiles.Single(t => t.Kind == "entries").Relation);
    }
}
