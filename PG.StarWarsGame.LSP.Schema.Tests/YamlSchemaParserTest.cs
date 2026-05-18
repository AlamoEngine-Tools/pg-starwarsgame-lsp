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
}