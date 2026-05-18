using PG.StarWarsGame.Files.XML;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Assets.Tests.Fakes;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

public sealed class GameSymbolProjectorTests
{
    private static readonly FakeSchemaProvider Schema = new(
        "CombatBonusAbility", "SFXEvent", "GameObjectType", "SpawnAbility");

    private static GameSymbolProjector Build()
    {
        return new GameSymbolProjector(Schema);
    }

    private static ProjectableEntry Entry(string name, string classification, string xmlFile = "DATA\\XML\\UNITS.XML",
        int? line = 5)
    {
        return new ProjectableEntry(name, classification, new XmlLocationInfo(xmlFile, line));
    }

    // ── Empty ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Project_NoEntries_ReturnsEmptySymbols()
    {
        var result = Build().Project([], [], null, "hash");
        Assert.Empty(result.Symbols);
    }

    // ── TypeName assignment ────────────────────────────────────────────────────

    [Fact]
    public void Project_KnownClassification_AssignsSchemaTypeName()
    {
        var entry = Entry("FOO_ABILITY", "COMBAT_BONUS_ABILITY");
        var result = Build().Project([entry], [], null, "hash");

        Assert.Equal("CombatBonusAbility", result.Symbols["FOO_ABILITY"].TypeName);
    }

    [Fact]
    public void Project_UnknownClassification_FallsBackToGameObjectType()
    {
        var entry = Entry("MY_UNIT", "MY_CUSTOM_UNIT_TYPE");
        var result = Build().Project([entry], [], null, "hash");

        Assert.Equal("GameObjectType", result.Symbols["MY_UNIT"].TypeName);
    }

    [Fact]
    public void Project_SfxEvent_TypeNameIsSFXEvent()
    {
        var entry = Entry("EXPLOSION_01", "SFXEVENT");
        var result = Build().Project([], [entry], null, "hash");

        Assert.Equal("SFXEvent", result.Symbols["EXPLOSION_01"].TypeName);
        Assert.Equal(GameSymbolKind.XmlObject, result.Symbols["EXPLOSION_01"].Kind);
    }

    // ── Symbol fields ──────────────────────────────────────────────────────────

    [Fact]
    public void Project_Symbol_HasCorrectIdAndKind()
    {
        var entry = Entry("UNIT_ALPHA", "COMBAT_BONUS_ABILITY");
        var result = Build().Project([entry], [], null, "hash");

        var sym = result.Symbols["UNIT_ALPHA"];
        Assert.Equal("UNIT_ALPHA", sym.Id);
        Assert.Equal(GameSymbolKind.XmlObject, sym.Kind);
        Assert.Null(sym.Description);
    }

    // ── Origin resolution ─────────────────────────────────────────────────────

    [Fact]
    public void Project_NonEmptyXmlFile_ProducesFileOrigin()
    {
        var entry = Entry("OBJ_A", "SPAWN_ABILITY", "DATA\\XML\\ABILITIES.XML", 42);
        var result = Build().Project([entry], [], null, "hash");

        var origin = Assert.IsType<FileOrigin>(result.Symbols["OBJ_A"].Origin);
        Assert.Equal("DATA\\XML\\ABILITIES.XML", origin.Uri);
        Assert.Equal(42, origin.Line);
    }

    [Fact]
    public void Project_EmptyXmlFile_ProducesUnknownOrigin()
    {
        var entry = Entry("OBJ_B", "SPAWN_ABILITY", "", null);
        var result = Build().Project([entry], [], null, "hash");

        Assert.IsType<UnknownOrigin>(result.Symbols["OBJ_B"].Origin);
    }

    [Fact]
    public void Project_NullLine_FileOriginLineIsZero()
    {
        var entry = Entry("OBJ_C", "SPAWN_ABILITY", "DATA\\XML\\X.XML", null);
        var result = Build().Project([entry], [], null, "hash");

        var origin = Assert.IsType<FileOrigin>(result.Symbols["OBJ_C"].Origin);
        Assert.Equal(0, origin.Line);
    }

    // ── Multiple entries ──────────────────────────────────────────────────────

    [Fact]
    public void Project_MultipleEntries_AllSymbolsPresent()
    {
        var objs = new[] { Entry("A", "COMBAT_BONUS_ABILITY"), Entry("B", "SPAWN_ABILITY") };
        var sfx = new[] { Entry("SFX_X", "SFXEVENT") };
        var result = Build().Project(objs, sfx, null, "hash");

        Assert.Equal(3, result.Symbols.Count);
        Assert.True(result.Symbols.ContainsKey("A"));
        Assert.True(result.Symbols.ContainsKey("B"));
        Assert.True(result.Symbols.ContainsKey("SFX_X"));
    }

    // ── SourceManifestHash ────────────────────────────────────────────────────

    [Fact]
    public void Project_SourceManifestHash_StoredInBaseline()
    {
        var result = Build().Project([], [], null, "abc123");
        Assert.Equal("abc123", result.SourceManifestHash);
    }

    // ── Dynamic enums ─────────────────────────────────────────────────────────

    [Fact]
    public void Project_GameConstantsXml_ExtractsDamageAndArmorTypes()
    {
        const string xml = """
                           <GameConstants>
                             <Damage_Types>EXPLOSIVE ENERGY GRENADE</Damage_Types>
                             <Armor_Types>ARMOR_INFANTRY ARMOR_VEHICLE</Armor_Types>
                           </GameConstants>
                           """;

        var result = Build().Project([], [], xml, "hash");

        Assert.Collection(result.DynamicEnumValues["DamageType"],
            v => Assert.Equal("EXPLOSIVE", v),
            v => Assert.Equal("ENERGY", v),
            v => Assert.Equal("GRENADE", v));
        Assert.Collection(result.DynamicEnumValues["ArmorType"],
            v => Assert.Equal("ARMOR_INFANTRY", v),
            v => Assert.Equal("ARMOR_VEHICLE", v));
    }

    [Fact]
    public void Project_NullGameConstantsXml_EmptyDynamicEnums()
    {
        var result = Build().Project([], [], null, "hash");
        Assert.Empty(result.DynamicEnumValues);
        Assert.Empty(result.HardcodedEnumValues);
    }

    [Fact]
    public void Project_GameConstantsXml_MissingSection_ReturnsEmpty()
    {
        const string xml = "<GameConstants><Max_Tech_Level>5</Max_Tech_Level></GameConstants>";
        var result = Build().Project([], [], xml, "hash");
        Assert.Empty(result.DynamicEnumValues);
        Assert.Empty(result.HardcodedEnumValues);
    }

    [Fact]
    public void Project_GameConstantsXml_NoBoundaryComment_HardcodedEnumValuesEmpty()
    {
        const string xml = """
                           <GameConstants>
                             <Damage_Types>EXPLOSIVE ENERGY GRENADE</Damage_Types>
                           </GameConstants>
                           """;
        var result = Build().Project([], [], xml, "hash");

        Assert.Empty(result.HardcodedEnumValues);
        Assert.Equal(3, result.DynamicEnumValues["DamageType"].Length);
    }

    [Fact]
    public void Project_GameConstantsXml_WithBoundaryComment_SplitsHardcodedValues()
    {
        const string xml = """
                           <GameConstants>
                             <Damage_Types>MOD_TYPE_A MOD_TYPE_B
                           <!-- PLEASE add your new damage types ABOVE this point. -->
                           EXPLOSIVE ENERGY GRENADE
                             </Damage_Types>
                             <Armor_Types>MOD_ARMOR
                           <!-- PLEASE add your new armor types ABOVE this point. -->
                           ARMOR_INFANTRY ARMOR_VEHICLE
                             </Armor_Types>
                           </GameConstants>
                           """;
        var result = Build().Project([], [], xml, "hash");

        // DynamicEnumValues contains ALL values (above + below boundary)
        Assert.Equal(5, result.DynamicEnumValues["DamageType"].Length);
        Assert.Equal(3, result.DynamicEnumValues["ArmorType"].Length);

        // HardcodedEnumValues contains only the below-boundary values
        Assert.Collection(result.HardcodedEnumValues["DamageType"],
            v => Assert.Equal("EXPLOSIVE", v),
            v => Assert.Equal("ENERGY", v),
            v => Assert.Equal("GRENADE", v));
        Assert.Collection(result.HardcodedEnumValues["ArmorType"],
            v => Assert.Equal("ARMOR_INFANTRY", v),
            v => Assert.Equal("ARMOR_VEHICLE", v));
    }

    [Fact]
    public void Project_GameConstantsXml_BoundaryCommentAtEnd_HardcodedEmpty()
    {
        const string xml = """
                           <GameConstants>
                             <Damage_Types>EXPLOSIVE ENERGY
                           <!-- PLEASE add your new damage types ABOVE this point. -->
                             </Damage_Types>
                           </GameConstants>
                           """;
        var result = Build().Project([], [], xml, "hash");

        Assert.False(result.HardcodedEnumValues.ContainsKey("DamageType"));
        Assert.Equal(2, result.DynamicEnumValues["DamageType"].Length);
    }
}