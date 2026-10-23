// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.ModBrowser;

namespace PG.StarWarsGame.LSP.Server.Tests.ModBrowser;

/// <summary>
///     What one entry opens into (#270): the entries its tags name, what names it, and every layer
///     that defines it - computed for that entry alone, when it is opened.
/// </summary>
public sealed class SymbolRelationsTest
{
    private const string Units = "file:///c:/work/mod/data/xml/units.xml";

    private const string BaseUnits = "file:///c:/work/base/data/xml/units.xml";

    // A dependency's object read out of its MEG: no file the editor can open.
    private const string PackedUnits = "meg:///c:/work/packed/data/config.meg/data/xml/units.xml";
    private const string Page = "xml:GameObjectType";

    private static GameSymbol Object(string id, int line, string type = "GameObjectType")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, type, new FileOrigin(Units, line, 4), null);
    }

    private static readonly Dictionary<string, XmlTagDefinition> s_tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space_Model_Name"] = Tag("Space_Model_Name", ReferenceKind.ModelFile),
        ["Damage_Particles"] = Tag("Damage_Particles", ReferenceKind.ModelFile),
        ["Death_Explosions"] = Tag("Death_Explosions", ReferenceKind.XmlObject, "Particle"),
        ["SFXEvent_Select"] = Tag("SFXEvent_Select", ReferenceKind.XmlObject, "SFXEvent"),
        ["SFXEvent_Move"] = Tag("SFXEvent_Move", ReferenceKind.XmlObject, "SFXEvent"),
        ["HardPoints"] = Tag("HardPoints", ReferenceKind.XmlObject, "HardPoint"),
        ["Icon_Name"] = Tag("Icon_Name", ReferenceKind.TextureFile),
        ["Lua_Script"] = Tag("Lua_Script", ReferenceKind.WorkspaceFile),
        ["Max_Speed"] = Tag("Max_Speed", ReferenceKind.None)
    };

    private static XmlTagDefinition Tag(string name, ReferenceKind kind, string? type = null)
    {
        return new XmlTagDefinition
        {
            Tag = name, ValueType = XmlValueType.NameReference, ReferenceKind = kind, ReferenceTypeName = type
        };
    }

    private sealed class Schema : NullSchemaProvider, ISchemaProvider
    {
        private static readonly ObjectKindDefinition s_planet = new()
        {
            Kind = ObjectKindNames.Planet, Behaviors = ["PLANET"]
        };

        public override XmlTagDefinition? GetTag(string tagName)
        {
            return s_tags.GetValueOrDefault(tagName);
        }

        public ObjectKindDefinition? GetKind(string kindName)
        {
            return string.Equals(kindName, ObjectKindNames.Planet, StringComparison.OrdinalIgnoreCase)
                ? s_planet
                : null;
        }
    }

    private sealed class Tags(Dictionary<string, (string Tag, string Value)[]> tags) : IVariantTagSource
    {
        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return tags.TryGetValue(objectId, out var found)
                ? found.Select((t, i) => new VariantTag(t.Tag, t.Value, $"<{t.Tag}>{t.Value}</{t.Tag}>", i)).ToList()
                : null;
        }
    }

    private static SymbolRelations Relations(bool withDependencies = false)
    {
        GameSymbol[] symbols =
        [
            // An ability inside the TIE fighter is indexed as a symbol of its own, between it and the
            // reference; the reference is still the TIE fighter's (measured on FoC: Piet's bonus).
            Object("Star_Destroyer", 3), Object("Tie_Fighter", 20), Object("Tie_Fighter$Tractor_Beam", 22),
            Object("Proton_Explosion", 40, "Particle"),
            Object("Unit_Select_SD", 50, "SFXEvent"), Object("HP_Laser_1", 60, "HardPoint"),
            Object("Star_Destroyer_Mk2", 70) with { VariantBaseId = "Star_Destroyer" },
            // A planet whose text defines a body, as the vanilla planets' do.
            new GameSymbol("Coruscant", GameSymbolKind.XmlObject, "GameObjectType", new FileOrigin(Units, 80, 4),
                null, null, ["PLANET"])
        ];
        // The baseline records where the game defines a symbol by its game-relative path (measured
        // on FoC: DATA\XML\SPACEUNITSCAPITAL.XML), which is no document of the project.
        var baseline = BaselineIndex.Empty with
        {
            Symbols = ImmutableDictionary<string, GameSymbol>.Empty
                .Add("Star_Destroyer", new GameSymbol("Star_Destroyer", GameSymbolKind.XmlObject, "GameObjectType",
                    new FileOrigin(@"DATA\XML\UNITS.XML", 3, 4), null))
                .Add("Shipped_Only", new GameSymbol("Shipped_Only", GameSymbolKind.XmlObject, "GameObjectType",
                    new FileOrigin(@"DATA\XML\UNITS.XML", 12, 4), null))
        };
        var tieUsesSd =
            new GameReference("Star_Destroyer", GameSymbolKind.XmlObject, "GameObjectType", Units, 25, 8, 14);
        var tieUsesShipped =
            new GameReference("Shipped_Only", GameSymbolKind.XmlObject, "GameObjectType", Units, 26, 8, 12);
        // Two dependencies under the mod that define the Star Destroyer as well: one in a loose file,
        // one packed. The mod's definition wins; theirs are what it overrides.
        GameSymbol[] baseDefinitions =
        [
            new("Star_Destroyer", GameSymbolKind.XmlObject, "GameObjectType", new FileOrigin(BaseUnits, 9, 4), null),
            new("Star_Destroyer", GameSymbolKind.XmlObject, "GameObjectType", new FileOrigin(PackedUnits, 5, 4), null)
        ];
        var documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add(Units,
            new DocumentIndex(Units, 1, [.. symbols], [tieUsesSd, tieUsesShipped], LayerRank: 0, LayerName: "Mod"));
        var definitions = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s));
        if (withDependencies)
        {
            documents = documents
                .Add(BaseUnits,
                    new DocumentIndex(BaseUnits, 1, [baseDefinitions[0]], [], LayerRank: -1, LayerName: "Base"))
                .Add(PackedUnits,
                    new DocumentIndex(PackedUnits, 1, [baseDefinitions[1]], [], LayerRank: -2, LayerName: "Packed"));
            definitions = definitions.SetItem("Star_Destroyer",
                [baseDefinitions[1], definitions["Star_Destroyer"][0], baseDefinitions[0]]);
        }

        var index = GameIndex.Empty with
        {
            Baseline = baseline,
            Documents = documents,
            WorkspaceDefinitions = definitions,
            WorkspaceReferences = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("Star_Destroyer", [tieUsesSd]).Add("Shipped_Only", [tieUsesShipped]),
            AssetFiles = MergedAssetFileIndex.Merge(
            [
                "data/art/models/ev_stardestroyer.alo", "data/art/models/p_engine_glow.alo",
                "data/art/textures/i_button_sd.tga"
            ], []),
            ParticleSystems = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "p_engine_glow.alo")
        };
        var tags = new Tags(new Dictionary<string, (string, string)[]>
        {
            ["Star_Destroyer"] =
            [
                ("Space_Model_Name", "EV_StarDestroyer.alo"), ("Damage_Particles", "P_Engine_Glow.alo"),
                ("Death_Explosions", "Proton_Explosion"), ("SFXEvent_Select", "Unit_Select_SD"),
                ("HardPoints", "HP_Laser_1, HP_Missing"), ("Icon_Name", "I_Button_SD.tga"),
                ("Lua_Script", "StarDestroyer"), ("Max_Speed", "2.0")
            ],
            ["Tie_Fighter"] = [("Space_Model_Name", "EV_Missing.alo"), ("Encyclopedia_Text", "TEXT_TIE")],
            ["Coruscant"] = [("Encyclopedia_Text", "TEXT_CORUSCANT")]
        });
        var schema = new Schema();
        return new SymbolRelations(index, schema, tags, SymbolCatalog.Build(index, [], []));
    }

    private static Dictionary<string, SymbolRelation> RelationsOf(string id)
    {
        return Relations().Node(Page, id).Relations.ToDictionary(r => r.Relation);
    }

    private static List<(string Action, string? Reason)> ActionsOf(SymbolRelations relations, string id)
    {
        return relations.Node(Page, id).Actions.Select(a => (a.Action, a.Reason)).ToList();
    }

    [Fact]
    public void AnOpenedObject_OffersTheCodeLensActions_TheOnesItHasNothingFor_WithTheReason()
    {
        Assert.Equal(
            [
                ("definition", null), ("previewUnit", null), ("encyclopedia", "noEncyclopedia"),
                ("effectiveObject", "notAVariant")
            ],
            ActionsOf(Relations(), "Star_Destroyer"));
    }

    // A planet's popup is a preview of its own, not the unit card (maintainer, 2026-10-22): the card
    // is not offered for one, though its text defines a body.
    [Fact]
    public void APlanet_HasNoEncyclopediaCard_AnObjectWithBodyTextHasOne()
    {
        var relations = Relations();

        Assert.Contains(("encyclopedia", "planet"), ActionsOf(relations, "Coruscant"));
        Assert.Contains(("encyclopedia", null), ActionsOf(relations, "Tie_Fighter"));
    }

    [Fact]
    public void AVariant_ShowsItsEffectiveObject_AndPreviewsTheModelItInherits()
    {
        var actions = ActionsOf(Relations(), "Star_Destroyer_Mk2");

        Assert.Contains(("previewUnit", null), actions);
        Assert.Contains(("effectiveObject", null), actions);
    }

    // Assembling a unit and its encyclopedia card mean nothing to an SFX event, so they are not on
    // its list at all; what does apply stays, disabled with its reason when unavailable.
    [Fact]
    public void AnObjectOfAnotherType_OffersOnlyTheActionsThatApplyToIt()
    {
        Assert.Equal([("definition", null), ("effectiveObject", "notAVariant")],
            Relations().Node("xml:SFXEvent", "Unit_Select_SD").Actions.Select(a => (a.Action, a.Reason)));
    }

    [Fact]
    public void AShippedObject_HasNoDefinitionToGoTo()
    {
        var relations = Relations();

        Assert.Null(relations.Node(Page, "Shipped_Only").Location);
        Assert.Contains(("definition", "shipped"), ActionsOf(relations, "Shipped_Only"));
    }

    [Fact]
    public void AGameObject_OpensIntoWhatItsTagsName_ByWhatEachIs()
    {
        var relations = RelationsOf("Star_Destroyer");

        Assert.Equal(
            [
                "models", "particleSystems", "particles", "sfxEvents", "speechEvents", "hardpoints", "otherReferences",
                "textures", "luaScript", "referencedBy", "overrides"
            ],
            relations.Keys);
        Assert.Equal(1, relations["models"].Count);
        Assert.Equal(1, relations["particleSystems"].Count);
        Assert.Equal(1, relations["particles"].Count);
        Assert.Equal(1, relations["sfxEvents"].Count);
        Assert.Equal(2, relations["hardpoints"].Count);
        Assert.Equal(1, relations["textures"].Count);
        Assert.Equal(1, relations["luaScript"].Count);
        Assert.Equal(1, relations["referencedBy"].Count);
        Assert.Equal(1, relations["overrides"].Count);
    }

    [Fact]
    public void AnEmptyRelation_IsCountedZero_WithItsReason()
    {
        var relations = RelationsOf("Star_Destroyer");

        Assert.Equal(new SymbolRelation("speechEvents", 0, "none"), relations["speechEvents"]);
        Assert.Null(relations["models"].Reason);
    }

    // The baseline holds no tags for this one: what it names is unknown, what names it is not.
    [Fact]
    public void AnObjectWithoutTags_HasNoTagRelations_ButIsStillReferenced()
    {
        var relations = RelationsOf("Shipped_Only");

        Assert.Equal(new SymbolRelation("models", 0, "notInModFiles"), relations["models"]);
        Assert.Equal(1, relations["referencedBy"].Count);
    }

    [Fact]
    public void ARelationsRows_AreTheEntriesItself_ANameNoneDefines_IsMissing()
    {
        var relations = Relations();

        var models = relations.Rows(Page, "Star_Destroyer", "models")!;
        var hardpoints = relations.Rows(Page, "Star_Destroyer", "hardpoints")!;
        var referencedBy = relations.Rows(Page, "Star_Destroyer", "referencedBy")!;

        Assert.Equal(("ev_stardestroyer.alo", "game"), (models.Single().Name, models.Single().Origin));
        Assert.Equal([("HP_Laser_1", "layer:0"), ("HP_Missing", "missing")],
            hardpoints.Select(r => (r.Name, r.Origin)));
        Assert.Equal("Tie_Fighter", referencedBy.Single().Name);
    }

    [Fact]
    public void TheOverrides_AreTheDefinitionsTheWinnerReplaces_HighestFirst()
    {
        var overrides = Relations(withDependencies: true).Rows(Page, "Star_Destroyer", "overrides")!;

        Assert.Equal(["layer:-1", "layer:-2", "game"], overrides.Select(r => r.Origin));
    }

    [Fact]
    public void AnOverride_InAFile_GoesToIt_APackedOne_SaysSo()
    {
        var overrides = Relations(withDependencies: true).Rows(Page, "Star_Destroyer", "overrides")!;

        Assert.Equal([new SymbolLocation(BaseUnits, 9), null, null], overrides.Select(r => r.Location));
        Assert.Equal([false, true, true], overrides.Select(r => r.Packed));
    }

    [Fact]
    public void AnObjectDefinedOnce_OverridesNothing_AndSaysSo()
    {
        Assert.Equal(new SymbolRelation("overrides", 0, "definedOnce"), RelationsOf("Tie_Fighter")["overrides"]);
    }

    [Fact]
    public void AnOpenedObject_SaysWhereTheDefinitionTheGameUsesIs_AndWhatItIsAVariantOf()
    {
        var relations = Relations();

        var destroyer = relations.Node(Page, "Star_Destroyer");
        var variant = relations.Node(Page, "Star_Destroyer_Mk2");
        var shipped = relations.Node(Page, "Shipped_Only");

        Assert.Equal(new SymbolLocation(Units, 3), destroyer.Location);
        Assert.Null(destroyer.VariantOf);
        Assert.Equal("Star_Destroyer", variant.VariantOf);
        Assert.Null(shipped.Location);
    }

    [Fact]
    public void ARelationsRow_OpensOnItsOwnPage_AMissingOrAnOverrideOnNone()
    {
        var relations = Relations();

        Assert.Equal("models", relations.Rows(Page, "Star_Destroyer", "models")!.Single().Page);
        Assert.Equal(["xml:HardPoint", null],
            relations.Rows(Page, "Star_Destroyer", "hardpoints")!.Select(r => r.Page));
        Assert.All(relations.Rows(Page, "Star_Destroyer", "overrides")!, r => Assert.Null(r.Page));
    }

    [Fact]
    public void AnUnknownEntryOrRelation_HasNothing()
    {
        Assert.Null(Relations().Node(Page, "No_Such_Object").Entry);
        Assert.Null(Relations().Rows(Page, "Star_Destroyer", "noSuchRelation"));
    }
}