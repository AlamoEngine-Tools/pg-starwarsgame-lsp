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

    private static readonly EnumDefinition s_damage = new()
    {
        Name = "DamageType",
        Values =
        [
            new EnumValueDefinition { Name = "Damage_Laser", Description = En("Lasers") },
            new EnumValueDefinition { Name = "Damage_Ion" }
        ]
    };

    private static readonly ObjectKindDefinition s_spaceUnit = new()
    {
        Kind = "SpaceUnit", Behaviors = ["SPACE_UNIT"], Flags = ["Is_Space"], Description = En("Flies in space")
    };

    private sealed class Schema : NullSchemaProvider
    {
        public override IReadOnlyList<ObjectKindDefinition> AllKinds => [s_spaceUnit];

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
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Units, new DocumentIndex(Units, 1, [destroyer], [], LayerRank: 0, LayerName: "Mod")),
            WorkspaceDefinitions =
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(destroyer.Id, [destroyer])
        };
        var schema = new Schema();
        var catalog = SymbolCatalog.Build(index, [s_spaceUnit], [s_damage], schema);
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
                new SymbolPageSummary("schemaTypes", 2), new SymbolPageSummary("enums", 1),
                new SymbolPageSummary("behaviours", 1)
            ],
            catalog.Outline(SymbolOutlines.Schema));
    }

    [Fact]
    public void AType_OpensIntoItsTags_EachByWhatItTakes()
    {
        var (catalog, relations, _) = Build();

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
        Assert.Equal([("Damage_Laser", "Lasers"), ("Damage_Ion", null)],
            damage.Tiles[2].Entries!.Select(e => (e.Name, e.Detail)));
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