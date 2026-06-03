// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class YamlSchemaParserTest
{
    // ── ParseTagFile ────────────────────────────────────────────────────────

    [Fact]
    public void ParseTagFile_AllFields_MappedCorrectly()
    {
        const string yaml = """
                            tags:
                              - tag: Tactical_Health
                                type: Float
                                referenceType: FooRef
                                enumName: BarEnum
                                deprecated: true
                                availableSince: "EaW 1.0"
                                multipleAllowed: true
                                description:
                                  en: "Health points"
                            """;

        var tags = YamlSchemaParser.ParseTagFile(yaml);

        var tag = Assert.Single(tags);
        Assert.Equal("Tactical_Health", tag.Tag);
        Assert.Equal(XmlValueType.Float, tag.ValueType);
        Assert.Equal("FooRef", tag.ReferenceType);
        Assert.Equal("BarEnum", tag.EnumName);
        Assert.True(tag.Deprecated);
        Assert.Equal("EaW 1.0", tag.AvailableSince);
        Assert.True(tag.MultipleAllowed);
        Assert.Equal("Health points", tag.Description["en"]);
    }

    [Fact]
    public void ParseTagFile_MultipleAllowed_DefaultsFalse()
    {
        const string yaml = """
                            tags:
                              - tag: Speed
                                type: Float
                            """;

        var tag = Assert.Single(YamlSchemaParser.ParseTagFile(yaml));

        Assert.False(tag.MultipleAllowed);
    }

    [Fact]
    public void ParseTagFile_MinimalEntry_OptionalFieldsDefaulted()
    {
        const string yaml = """
                            tags:
                              - tag: Speed
                                type: Float
                            """;

        var tag = Assert.Single(YamlSchemaParser.ParseTagFile(yaml));

        Assert.Equal("Speed", tag.Tag);
        Assert.False(tag.Deprecated);
        Assert.False(tag.MultipleAllowed);
        Assert.Null(tag.ReferenceType);
        Assert.Null(tag.EnumName);
        Assert.Null(tag.AvailableSince);
        Assert.Empty(tag.Description);
    }

    [Fact]
    public void ParseTagFile_UnknownType_EntrySkipped()
    {
        const string yaml = """
                            tags:
                              - tag: GoodTag
                                type: Float
                              - tag: BadTag
                                type: NotARealType
                            """;

        var tags = YamlSchemaParser.ParseTagFile(yaml);

        var tag = Assert.Single(tags);
        Assert.Equal("GoodTag", tag.Tag);
    }

    [Fact]
    public void ParseTagFile_TypeParsedCaseInsensitive()
    {
        const string yaml = """
                            tags:
                              - tag: Foo
                                type: float
                            """;

        var tag = Assert.Single(YamlSchemaParser.ParseTagFile(yaml));
        Assert.Equal(XmlValueType.Float, tag.ValueType);
    }

    [Fact]
    public void ParseTagFile_EmptyList_ReturnsEmpty()
    {
        const string yaml = """
                            tags: []
                            """;

        Assert.Empty(YamlSchemaParser.ParseTagFile(yaml));
    }

    [Fact]
    public void ParseTagFile_ValueGroup_Scalar_ParsedAsSingleElementList()
    {
        const string yaml = """
                            tags:
                              - tag: Primary_Locomotor_Name
                                type: NameReference
                                valueGroup: Locomotor
                            """;

        var tag = Assert.Single(YamlSchemaParser.ParseTagFile(yaml));

        Assert.Equal(["Locomotor"], tag.ValueGroups);
    }

    [Fact]
    public void ParseTagFile_ValueGroup_List_ParsedAsMultipleElements()
    {
        const string yaml = """
                            tags:
                              - tag: LandBehavior
                                type: TypeReferenceList
                                valueGroup: [Land, Space]
                            """;

        var tag = Assert.Single(YamlSchemaParser.ParseTagFile(yaml));

        Assert.Equal(["Land", "Space"], tag.ValueGroups);
    }

    [Fact]
    public void ParseTagFile_NoValueGroup_DefaultsToEmptyList()
    {
        const string yaml = """
                            tags:
                              - tag: Speed
                                type: Float
                            """;

        var tag = Assert.Single(YamlSchemaParser.ParseTagFile(yaml));

        Assert.Empty(tag.ValueGroups);
    }

    [Fact]
    public void ParseTagFile_MultipleLocales_AllPreserved()
    {
        const string yaml = """
                            tags:
                              - tag: Foo
                                type: Float
                                description:
                                  en: "English"
                                  de: "Deutsch"
                            """;

        var tag = Assert.Single(YamlSchemaParser.ParseTagFile(yaml));
        Assert.Equal("English", tag.Description["en"]);
        Assert.Equal("Deutsch", tag.Description["de"]);
    }

    // ── ParseTypeFile ───────────────────────────────────────────────────────

    [Fact]
    public void ParseTypeFile_WithNameTag_Parsed()
    {
        const string yaml = """
                            types:
                              - typeName: GameObjectType
                                nameTag: Name
                            """;

        var type = Assert.Single(YamlSchemaParser.ParseTypeFile(yaml));
        Assert.Equal("GameObjectType", type.TypeName);
        Assert.Equal("Name", type.NameTag);
    }

    [Fact]
    public void ParseTypeFile_SingletonType_NullNameTag()
    {
        const string yaml = """
                            types:
                              - typeName: GameConstants
                            """;

        var type = Assert.Single(YamlSchemaParser.ParseTypeFile(yaml));
        Assert.Equal("GameConstants", type.TypeName);
        Assert.Null(type.NameTag);
    }

    [Fact]
    public void ParseTypeFile_Description_Parsed()
    {
        const string yaml = """
                            types:
                              - typeName: Campaign
                                nameTag: Name
                                description:
                                  en: "A campaign definition."
                            """;

        var type = Assert.Single(YamlSchemaParser.ParseTypeFile(yaml));
        Assert.Equal("A campaign definition.", type.Description["en"]);
    }

    [Fact]
    public void ParseTypeFile_EmptyList_ReturnsEmpty()
    {
        const string yaml = """
                            types: []
                            """;

        Assert.Empty(YamlSchemaParser.ParseTypeFile(yaml));
    }

    // ── ParseHardcodedSetFile ───────────────────────────────────────────────

    [Fact]
    public void ParseHardcodedSetFile_AllFields_MappedCorrectly()
    {
        const string yaml = """
                            name: BehaviorModule
                            description:
                              en: "Known C++ behaviour module names."
                            deprecated: true
                            availableSince: "EaW 1.0"
                            values:
                              - name: GenericTransport
                                description:
                                  en: "Generic transport."
                                deprecated: false
                                availableSince: "EaW 1.0"
                                groups:
                                  - space
                                  - land
                            """;

        var set = YamlSchemaParser.ParseHardcodedSetFile(yaml);

        Assert.Equal("BehaviorModule", set.Name);
        Assert.Equal("Known C++ behaviour module names.", set.Description["en"]);
        Assert.True(set.Deprecated);
        Assert.Equal("EaW 1.0", set.AvailableSince);

        var value = Assert.Single(set.Values);
        Assert.Equal("GenericTransport", value.Name);
        Assert.Equal("Generic transport.", value.Description["en"]);
        Assert.False(value.Deprecated);
        Assert.Equal("EaW 1.0", value.AvailableSince);
        Assert.Equal(["space", "land"], value.Groups);
    }

    [Fact]
    public void ParseHardcodedSetFile_MinimalEntry_DefaultsApplied()
    {
        const string yaml = """
                            name: BehaviorModule
                            values:
                              - name: GenericTransport
                            """;

        var set = YamlSchemaParser.ParseHardcodedSetFile(yaml);

        Assert.Equal("BehaviorModule", set.Name);
        Assert.False(set.Deprecated);
        Assert.Null(set.AvailableSince);
        Assert.Empty(set.Description);

        var value = Assert.Single(set.Values);
        Assert.Equal("GenericTransport", value.Name);
        Assert.False(value.Deprecated);
        Assert.Null(value.AvailableSince);
        Assert.Empty(value.Groups);
    }

    [Fact]
    public void ParseHardcodedSetFile_EmptyValues_ReturnsEmptyList()
    {
        const string yaml = """
                            name: BehaviorModule
                            values: []
                            """;

        var set = YamlSchemaParser.ParseHardcodedSetFile(yaml);

        Assert.Equal("BehaviorModule", set.Name);
        Assert.Empty(set.Values);
    }

    // ── ParseMetafileFile ───────────────────────────────────────────────────

    [Fact]
    public void ParseMetafileFile_FileRegistryEntry_MappedCorrectly()
    {
        const string yaml = """
                            metafiles:
                              - path: data/xml/gameobjectfiles.xml
                                metaFileType: fileRegistry
                                types:
                                  - GameObjectType
                            """;

        var metafiles = YamlSchemaParser.ParseMetafileFile(yaml);

        var entry = Assert.Single(metafiles);
        Assert.Equal("data/xml/gameobjectfiles.xml", entry.Path);
        Assert.Equal(MetafileType.FileRegistry, entry.MetafileType);
        Assert.Equal(["GameObjectType"], entry.Types);
    }

    [Fact]
    public void ParseMetafileFile_DirectContentEntry_MappedCorrectly()
    {
        const string yaml = """
                            metafiles:
                              - path: data/xml/movies.xml
                                metaFileType: directContent
                                types:
                                  - BinkMovie
                            """;

        var metafiles = YamlSchemaParser.ParseMetafileFile(yaml);

        var entry = Assert.Single(metafiles);
        Assert.Equal(MetafileType.DirectContent, entry.MetafileType);
    }

    [Fact]
    public void ParseMetafileFile_SpecialEntry_MappedCorrectly()
    {
        const string yaml = """
                            metafiles:
                              - path: data/xml/campaignfiles.xml
                                metaFileType: special
                                types:
                                  - StoryParser
                            """;

        var metafiles = YamlSchemaParser.ParseMetafileFile(yaml);

        var entry = Assert.Single(metafiles);
        Assert.Equal(MetafileType.Special, entry.MetafileType);
    }

    [Fact]
    public void ParseMetafileFile_UpperCaseBackslashPath_NormalisedToLowercaseForwardSlash()
    {
        const string yaml = """
                            metafiles:
                              - path: DATA\XML\GameObjectFiles.xml
                                metaFileType: fileRegistry
                                types:
                                  - GameObjectType
                            """;

        var metafiles = YamlSchemaParser.ParseMetafileFile(yaml);

        Assert.Equal("data/xml/gameobjectfiles.xml", metafiles[0].Path);
    }

    // ── ParseEnumFile ───────────────────────────────────────────────────────

    [Fact]
    public void ParseEnumFile_AllFields_MappedCorrectly()
    {
        const string yaml = """
                            name: StoryEventType
                            description:
                              en: "Trigger condition type."
                            deprecated: false
                            availableSince: "FoC 1.0"
                            values:
                              - name: STORY_CONQUER
                                description:
                                  en: "Fires when control of a planet changes."
                                deprecated: false
                            """;

        var def = YamlSchemaParser.ParseEnumFile(yaml);

        Assert.Equal("StoryEventType", def.Name);
        Assert.Equal("Trigger condition type.", def.Description["en"]);
        Assert.Equal("FoC 1.0", def.AvailableSince);

        var value = Assert.Single(def.Values);
        Assert.Equal("STORY_CONQUER", value.Name);
        Assert.Equal("Fires when control of a planet changes.", value.Description["en"]);
    }

    [Fact]
    public void ParseEnumFile_WithPositionedParams_ReturnsParamDefinitionsAtCorrectPositions()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_CONQUER
                                params:
                                  - position: 0
                                    type: NameReferenceList
                                    referenceType: Planet
                                    description:
                                      en: "Planet(s) to watch."
                                  - position: 2
                                    type: DynamicEnumValue
                                    enumName: StoryEventFilter
                                    optional: true
                            """;

        var def = YamlSchemaParser.ParseEnumFile(yaml);
        var value = Assert.Single(def.Values);

        Assert.NotNull(value.Params);
        Assert.Equal(2, value.Params!.Count);

        var p0 = value.Params.Single(p => p.Position == 0);
        Assert.Equal(XmlValueType.NameReferenceList, p0.ValueType);
        Assert.Equal("Planet", p0.ReferenceType);
        Assert.Equal("Planet(s) to watch.", p0.Description["en"]);
        Assert.False(p0.Optional);

        var p2 = value.Params.Single(p => p.Position == 2);
        Assert.Equal(XmlValueType.DynamicEnumValue, p2.ValueType);
        Assert.Equal("StoryEventFilter", p2.EnumName);
        Assert.True(p2.Optional);
    }

    [Fact]
    public void ParseEnumFile_WithoutParams_ReturnsNullParams()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_TRIGGER
                                description:
                                  en: "Fires immediately when prerequisites are met."
                            """;

        var def = YamlSchemaParser.ParseEnumFile(yaml);
        var value = Assert.Single(def.Values);

        Assert.Null(value.Params);
    }

    [Fact]
    public void ParseEnumFile_WithUnknownParamType_LogsWarningAndSkipsParam()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_CONQUER
                                params:
                                  - position: 0
                                    type: NameReferenceList
                                  - position: 1
                                    type: NotARealType
                            """;

        var def = YamlSchemaParser.ParseEnumFile(yaml);
        var value = Assert.Single(def.Values);

        Assert.NotNull(value.Params);
        var param = Assert.Single(value.Params!);
        Assert.Equal(0, param.Position);
        Assert.Equal(XmlValueType.NameReferenceList, param.ValueType);
    }

    [Fact]
    public void ParseEnumFile_WithValueNotes_ReturnsNotes()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_GARRISON_UNIT
                                description:
                                  en: "Fires when the specified unit is garrisoned."
                                notes:
                                  en: "Never used in vanilla. Parameters 2 and 3 are probably non-functional."
                            """;

        var def = YamlSchemaParser.ParseEnumFile(yaml);
        var value = Assert.Single(def.Values);

        Assert.Equal("Never used in vanilla. Parameters 2 and 3 are probably non-functional.",
            value.Notes["en"]);
    }

    [Fact]
    public void ParseEnumFile_WithEnumLevelNotes_ReturnsNotes()
    {
        const string yaml = """
                            name: StoryEventType
                            notes:
                              en: "FoC-only enum."
                            values: []
                            """;

        var def = YamlSchemaParser.ParseEnumFile(yaml);

        Assert.Equal("FoC-only enum.", def.Notes["en"]);
    }

    [Fact]
    public void ParseEnumFile_WithoutNotes_ReturnsEmptyNotes()
    {
        const string yaml = """
                            name: StoryEventType
                            values:
                              - name: STORY_TRIGGER
                            """;

        var def = YamlSchemaParser.ParseEnumFile(yaml);
        var value = Assert.Single(def.Values);

        Assert.Empty(value.Notes);
        Assert.Empty(def.Notes);
    }

    [Fact]
    public void ParseTagFile_WithNotes_ReturnsNotes()
    {
        const string yaml = """
                            tags:
                              - tag: Event_Type
                                type: DynamicEnumValue
                                enumName: StoryEventType
                                notes:
                                  en: "Determines the trigger condition for this event block."
                            """;

        var tag = Assert.Single(YamlSchemaParser.ParseTagFile(yaml));

        Assert.Equal("Determines the trigger condition for this event block.", tag.Notes["en"]);
    }

    [Fact]
    public void ParseTagFile_WithoutNotes_ReturnsEmptyNotes()
    {
        const string yaml = """
                            tags:
                              - tag: Speed
                                type: Float
                            """;

        var tag = Assert.Single(YamlSchemaParser.ParseTagFile(yaml));

        Assert.Empty(tag.Notes);
    }

    [Fact]
    public void ParseHardcodedSetFile_WithValueNotes_ReturnsNotes()
    {
        const string yaml = """
                            name: BehaviorModule
                            values:
                              - name: GenericTransport
                                notes:
                                  en: "Only available in space tactical mode."
                            """;

        var set = YamlSchemaParser.ParseHardcodedSetFile(yaml);
        var value = Assert.Single(set.Values);

        Assert.Equal("Only available in space tactical mode.", value.Notes["en"]);
    }

    [Fact]
    public void ParseHardcodedSetFile_WithSetLevelNotes_ReturnsNotes()
    {
        const string yaml = """
                            name: BehaviorModule
                            notes:
                              en: "Deprecated module list."
                            values: []
                            """;

        var set = YamlSchemaParser.ParseHardcodedSetFile(yaml);

        Assert.Equal("Deprecated module list.", set.Notes["en"]);
    }

    // ── ParseMetafileFile ───────────────────────────────────────────────────

    [Fact]
    public void ParseMetafileFile_MultipleEntries_AllParsed()
    {
        const string yaml = """
                            metafiles:
                              - path: data/xml/gameobjectfiles.xml
                                metaFileType: fileRegistry
                                types:
                                  - GameObjectType
                              - path: data/xml/movies.xml
                                metaFileType: directContent
                                types:
                                  - BinkMovie
                              - path: data/xml/musicevents.xml
                                metaFileType: directContent
                                types:
                                  - MusicEvent
                            """;

        var metafiles = YamlSchemaParser.ParseMetafileFile(yaml);

        Assert.Equal(3, metafiles.Count);
    }
}