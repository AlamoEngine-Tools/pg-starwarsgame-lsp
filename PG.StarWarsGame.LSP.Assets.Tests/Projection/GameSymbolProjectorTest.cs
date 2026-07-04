// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Files.XML;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Assets.Tests.Fakes;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

public sealed class GameSymbolProjectorTest
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
        var result = Build().Project([], [], "hash");
        Assert.Empty(result.Symbols);
    }

    // ── TypeName assignment ────────────────────────────────────────────────────

    [Fact]
    public void Project_KnownClassification_AssignsSchemaTypeName()
    {
        var entry = Entry("FOO_ABILITY", "COMBAT_BONUS_ABILITY");
        var result = Build().Project([entry], [], "hash");

        Assert.Equal("CombatBonusAbility", result.Symbols["FOO_ABILITY"].TypeName);
    }

    [Fact]
    public void Project_UnknownClassification_FallsBackToGameObjectType()
    {
        var entry = Entry("MY_UNIT", "MY_CUSTOM_UNIT_TYPE");
        var result = Build().Project([entry], [], "hash");

        Assert.Equal("GameObjectType", result.Symbols["MY_UNIT"].TypeName);
    }

    [Fact]
    public void Project_SfxEvent_TypeNameIsSFXEvent()
    {
        var entry = Entry("EXPLOSION_01", "SFXEVENT");
        var result = Build().Project([], [entry], "hash");

        Assert.Equal("SFXEvent", result.Symbols["EXPLOSION_01"].TypeName);
        Assert.Equal(GameSymbolKind.XmlObject, result.Symbols["EXPLOSION_01"].Kind);
    }

    [Fact]
    public void Project_MusicEvent_TypeNameIsMusicEvent()
    {
        var entry = Entry("MAIN_THEME", "MUSIC_EVENT");
        var result = Build().Project([], [], "hash", [entry]);

        Assert.Equal("MusicEvent", result.Symbols["MAIN_THEME"].TypeName);
        Assert.Equal(GameSymbolKind.XmlObject, result.Symbols["MAIN_THEME"].Kind);
    }

    [Fact]
    public void Project_NoMusicEventsArgument_DoesNotThrow_AndProjectsNoMusicEventSymbols()
    {
        var result = Build().Project([], [], "hash");

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public void Project_MusicEventWithTags_PopulatesObjectTags()
    {
        var entry = new ProjectableEntry("MAIN_THEME", "MUSIC_EVENT",
            new XmlLocationInfo("DATA\\XML\\MUSICEVENTS.XML", 5), [Tag("Volume_Percent", "70")]);
        var result = Build().Project([], [], "hash", [entry]);

        Assert.True(result.ObjectTags.ContainsKey("MAIN_THEME"));
        Assert.Contains(result.ObjectTags["MAIN_THEME"], t => t.TagName == "Volume_Percent" && t.Value == "70");
    }

    // ── Symbol fields ──────────────────────────────────────────────────────────

    [Fact]
    public void Project_Symbol_HasCorrectIdAndKind()
    {
        var entry = Entry("UNIT_ALPHA", "COMBAT_BONUS_ABILITY");
        var result = Build().Project([entry], [], "hash");

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
        var result = Build().Project([entry], [], "hash");

        var origin = Assert.IsType<FileOrigin>(result.Symbols["OBJ_A"].Origin);
        Assert.Equal("DATA\\XML\\ABILITIES.XML", origin.Uri);
        Assert.Equal(42, origin.Line);
    }

    [Fact]
    public void Project_EmptyXmlFile_ProducesUnknownOrigin()
    {
        var entry = Entry("OBJ_B", "SPAWN_ABILITY", "", null);
        var result = Build().Project([entry], [], "hash");

        Assert.IsType<UnknownOrigin>(result.Symbols["OBJ_B"].Origin);
    }

    [Fact]
    public void Project_NullLine_FileOriginLineIsZero()
    {
        var entry = Entry("OBJ_C", "SPAWN_ABILITY", "DATA\\XML\\X.XML", null);
        var result = Build().Project([entry], [], "hash");

        var origin = Assert.IsType<FileOrigin>(result.Symbols["OBJ_C"].Origin);
        Assert.Equal(0, origin.Line);
    }

    // ── Multiple entries ──────────────────────────────────────────────────────

    [Fact]
    public void Project_MultipleEntries_AllSymbolsPresent()
    {
        var objs = new[] { Entry("A", "COMBAT_BONUS_ABILITY"), Entry("B", "SPAWN_ABILITY") };
        var sfx = new[] { Entry("SFX_X", "SFXEVENT") };
        var result = Build().Project(objs, sfx, "hash");

        Assert.Equal(3, result.Symbols.Count);
        Assert.True(result.Symbols.ContainsKey("A"));
        Assert.True(result.Symbols.ContainsKey("B"));
        Assert.True(result.Symbols.ContainsKey("SFX_X"));
    }

    // ── SourceManifestHash ────────────────────────────────────────────────────

    [Fact]
    public void Project_SourceManifestHash_StoredInBaseline()
    {
        var result = Build().Project([], [], "abc123");
        Assert.Equal("abc123", result.SourceManifestHash);
    }

    // ── Object tag trees (variant inheritance support) ───────────────────────

    private static BaselineTag Tag(string name, string value)
    {
        return new BaselineTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private static ProjectableEntry EntryWithTags(string name, string classification, params BaselineTag[] tags)
    {
        return new ProjectableEntry(name, classification, new XmlLocationInfo("DATA\\XML\\UNITS.XML", 5), tags);
    }

    [Fact]
    public void Project_EntryWithTags_PopulatesObjectTags()
    {
        var entry = EntryWithTags("UNIT_A", "COMBAT_BONUS_ABILITY",
            Tag("Max_Health", "100"), Tag("Mass", "5"));
        var result = Build().Project([entry], [], "hash");

        Assert.True(result.ObjectTags.ContainsKey("UNIT_A"));
        Assert.Equal(2, result.ObjectTags["UNIT_A"].Length);
        Assert.Contains(result.ObjectTags["UNIT_A"], t => t.TagName == "Max_Health" && t.Value == "100");
    }

    [Fact]
    public void Project_EntryWithoutTags_NoObjectTagsEntry()
    {
        var entry = Entry("UNIT_B", "COMBAT_BONUS_ABILITY");
        var result = Build().Project([entry], [], "hash");

        Assert.False(result.ObjectTags.ContainsKey("UNIT_B"));
    }

    [Fact]
    public void Project_VariantTag_SetsVariantBaseIdOnSymbol()
    {
        var schema = new FakeSchemaProvider("CombatBonusAbility");
        schema.VariantTagNames.Add("Variant_Of_Existing_Type");
        var projector = new GameSymbolProjector(schema);

        var entry = EntryWithTags("VARIANT_A", "COMBAT_BONUS_ABILITY",
            Tag("Variant_Of_Existing_Type", "BASE_OBJ"));
        var result = projector.Project([entry], [], "hash");

        Assert.Equal("BASE_OBJ", result.Symbols["VARIANT_A"].VariantBaseId);
    }

    [Fact]
    public void Project_NonVariantEntry_VariantBaseIdNull()
    {
        var entry = EntryWithTags("UNIT_C", "COMBAT_BONUS_ABILITY", Tag("Max_Health", "100"));
        var result = Build().Project([entry], [], "hash");

        Assert.Null(result.Symbols["UNIT_C"].VariantBaseId);
    }

    // ── Dynamic enum extraction is delegated to DynamicEnumExtractor ──────────

    [Fact]
    public void Project_AlwaysReturnsEmptyDynamicAndHardcodedEnums()
    {
        var result = Build().Project([], [], "hash");
        Assert.Empty(result.DynamicEnumValues);
        Assert.Empty(result.HardcodedEnumValues);
    }
}