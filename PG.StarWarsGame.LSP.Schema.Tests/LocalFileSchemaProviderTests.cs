using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Providers;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class LocalFileSchemaProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public LocalFileSchemaProviderTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private LocalFileSchemaProvider CreateAndLoad(
        string? tagsYaml = null, string? tagsFileName = "tags.yaml",
        string? tagsYaml2 = null, string? tagsFileName2 = null,
        string? typesYaml = null)
    {
        var tagsDir = Path.Combine(_tempDir, "tags");
        if (tagsYaml is not null)
        {
            Directory.CreateDirectory(tagsDir);
            File.WriteAllText(Path.Combine(tagsDir, tagsFileName!), tagsYaml);
        }
        if (tagsYaml2 is not null)
        {
            Directory.CreateDirectory(tagsDir);
            File.WriteAllText(Path.Combine(tagsDir, tagsFileName2!), tagsYaml2);
        }
        if (typesYaml is not null)
            File.WriteAllText(Path.Combine(_tempDir, "types.yaml"), typesYaml);
        // Constructor calls Load(); explicit call here ensures we re-read after all files are written
        var provider = new LocalFileSchemaProvider(_tempDir, NullLogger<LocalFileSchemaProvider>.Instance);
        provider.Load();
        return provider;
    }

    [Fact]
    public void ParseTagFile_NameReferenceWithReferenceType_Parsed()
    {
        const string yaml = """
                            tags:
                              - tag: Affiliation
                                type: NameReferenceList
                                referenceType: Faction
                            """;

        using var provider = CreateAndLoad(yaml);

        var tag = provider.GetTag("Affiliation");

        Assert.NotNull(tag);
        Assert.Equal("Affiliation", tag.Tag);
        Assert.Equal(XmlValueType.NameReferenceList, tag.ValueType);
        Assert.Equal("Faction", tag.ReferenceType);
    }

    [Fact]
    public void ParseTagFile_SFXEventReference_Parsed()
    {
        const string yaml = """
                            tags:
                              - tag: SFXEvent_Select
                                type: SFXEventReference
                                referenceType: SFXEvent
                            """;

        using var provider = CreateAndLoad(yaml);

        var tag = provider.GetTag("SFXEvent_Select");

        Assert.NotNull(tag);
        Assert.Equal(XmlValueType.SFXEventReference, tag.ValueType);
        Assert.Equal("SFXEvent", tag.ReferenceType);
    }

    [Fact]
    public void ParseTagFile_DynamicEnumValue_EnumNamePreserved()
    {
        const string yaml = """
                            tags:
                              - tag: CategoryMask
                                type: DynamicEnumValue
                                enumName: GameObjectCategoryType
                            """;

        using var provider = CreateAndLoad(yaml);

        var tag = provider.GetTag("CategoryMask");

        Assert.NotNull(tag);
        Assert.Equal(XmlValueType.DynamicEnumValue, tag.ValueType);
        Assert.Equal("GameObjectCategoryType", tag.EnumName);
    }

    [Fact]
    public void ParseTagFile_DeprecatedAndAvailableSince_Parsed()
    {
        const string yaml = """
                            tags:
                              - tag: Old_Tag
                                type: Float
                                deprecated: true
                                availableSince: "FoC 1.0"
                            """;

        using var provider = CreateAndLoad(yaml);

        var tag = provider.GetTag("Old_Tag");

        Assert.NotNull(tag);
        Assert.True(tag.Deprecated);
        Assert.Equal("FoC 1.0", tag.AvailableSince);
    }

    [Fact]
    public void ParseTypeFile_GameObjectType_Parsed()
    {
        const string yaml = """
                            types:
                              - typeName: GameObjectType
                                nameTag: Name
                            """;

        using var provider = CreateAndLoad(typesYaml: yaml);

        var type = provider.GetObjectType("GameObjectType");

        Assert.NotNull(type);
        Assert.Equal("GameObjectType", type.TypeName);
        Assert.Equal("Name", type.NameTag);
    }

    [Fact]
    public void NullNameTag_ParsedCorrectly()
    {
        const string yaml = """
                            types:
                              - typeName: GameConstants
                            """;

        using var provider = CreateAndLoad(typesYaml: yaml);

        var type = provider.GetObjectType("GameConstants");

        Assert.NotNull(type);
        Assert.Null(type.NameTag);
    }

    [Fact]
    public void LoadDirectory_MultipleYamlFiles_AllTagsAndTypesLoaded()
    {
        const string tagsYaml = """
                                tags:
                                  - tag: Tactical_Health
                                    type: Float
                                  - tag: Affiliation
                                    type: NameReferenceList
                                    referenceType: Faction
                                """;
        const string typesYaml = """
                                 types:
                                   - typeName: GameObjectType
                                     nameTag: Name
                                   - typeName: HardPoint
                                     nameTag: Name
                                   - typeName: Faction
                                     nameTag: Name
                                 """;

        using var provider = CreateAndLoad(tagsYaml, typesYaml: typesYaml);

        Assert.Equal(2, provider.AllTags.Count);
        Assert.Equal(3, provider.AllObjectTypes.Count);
    }

    [Fact]
    public void GetTag_CaseInsensitive_ReturnsTag()
    {
        const string yaml = """
                            tags:
                              - tag: Tactical_Health
                                type: Float
                            """;

        using var provider = CreateAndLoad(yaml);

        Assert.NotNull(provider.GetTag("tactical_health"));
        Assert.NotNull(provider.GetTag("TACTICAL_HEALTH"));
        Assert.NotNull(provider.GetTag("Tactical_Health"));
    }

    [Fact]
    public void UnknownType_IsSkipped_NoExceptionTagAbsent()
    {
        const string yaml = """
                            tags:
                              - tag: GoodTag
                                type: Float
                              - tag: BadTag
                                type: UnknownType
                            """;

        var ex = Record.Exception(() =>
        {
            using var provider = CreateAndLoad(yaml);
            var tag = Assert.Single(provider.AllTags);
            Assert.Equal("GoodTag", tag.Tag);
            Assert.Null(provider.GetTag("BadTag"));
        });

        Assert.Null(ex);
    }

    [Fact]
    public void EmptyDirectory_ReturnsEmptyCollections()
    {
        using var provider = new LocalFileSchemaProvider(_tempDir, NullLogger<LocalFileSchemaProvider>.Instance);
        provider.Load();

        Assert.Empty(provider.AllTags);
        Assert.Empty(provider.AllObjectTypes);
    }

    [Fact]
    public void GetTagsForType_ReturnsTagSetForNamedFile()
    {
        const string yaml = """
                            tags:
                              - tag: Tactical_Health
                                type: Float
                              - tag: Shield_Points
                                type: Float
                            """;

        using var provider = CreateAndLoad(yaml, "GameObjectType.yaml");

        Assert.Equal(2, provider.GetTagsForType("GameObjectType").Count);
        Assert.Empty(provider.GetTagsForType("Faction"));
    }

    [Fact]
    public void GetTagsForType_CaseInsensitive()
    {
        const string yaml = """
                            tags:
                              - tag: Tactical_Health
                                type: Float
                              - tag: Shield_Points
                                type: Float
                            """;

        using var provider = CreateAndLoad(yaml, "GameObjectType.yaml");

        Assert.Equal(2, provider.GetTagsForType("gameobjecttype").Count);
        Assert.Equal(2, provider.GetTagsForType("GAMEOBJECTTYPE").Count);
    }

    [Fact]
    public void GetAllTagDefinitions_ReturnsOneEntryPerType()
    {
        const string gameObjectTags = """
                                      tags:
                                        - tag: Text_ID
                                          type: NameReference
                                      """;
        const string factionTags = """
                                   tags:
                                     - tag: Text_ID
                                       type: NameReference
                                   """;

        using var provider = CreateAndLoad(
            gameObjectTags, "GameObjectType.yaml",
            factionTags, "Faction.yaml");

        Assert.Equal(2, provider.GetAllTagDefinitions("Text_ID").Count);
    }
}