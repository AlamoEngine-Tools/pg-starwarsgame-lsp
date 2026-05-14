using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class SchemaIndexTests
{
    // ── helpers ────────────────────────────────────────────────────────────

    private static XmlTagDefinition Tag(string name)
    {
        return new XmlTagDefinition { Tag = name, ValueType = XmlValueType.Float };
    }

    private static GameObjectTypeDefinition Type(string name, string? nameTag = "Name")
    {
        return new GameObjectTypeDefinition { TypeName = name, NameTag = nameTag };
    }

    private static SchemaIndex Build(
        IEnumerable<(string type, IReadOnlyList<XmlTagDefinition> tags)> tagsByType,
        IEnumerable<GameObjectTypeDefinition>? types = null,
        IEnumerable<EnumDefinition>? enums = null)
    {
        return new SchemaIndex(tagsByType, types ?? [], enums ?? []);
    }

    // ── GetTag ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetTag_ExistingTag_ReturnsDefinition()
    {
        var index = Build([("Foo", [Tag("Health")])]);
        Assert.Equal("Health", index.GetTag("Health")!.Tag);
    }

    [Fact]
    public void GetTag_CaseInsensitive()
    {
        var index = Build([("Foo", [Tag("Tactical_Health")])]);
        Assert.NotNull(index.GetTag("tactical_health"));
        Assert.NotNull(index.GetTag("TACTICAL_HEALTH"));
    }

    [Fact]
    public void GetTag_Unknown_ReturnsNull()
    {
        var index = Build([("Foo", [Tag("Health")])]);
        Assert.Null(index.GetTag("Missing"));
    }

    [Fact]
    public void GetTag_SameTagInTwoTypes_ReturnsFirst()
    {
        var a = new XmlTagDefinition { Tag = "Text_ID", ValueType = XmlValueType.NameReference };
        var b = new XmlTagDefinition { Tag = "Text_ID", ValueType = XmlValueType.Float };
        var index = Build([("TypeA", [a]), ("TypeB", [b])]);
        // First one encountered is TypeA's
        Assert.Equal(XmlValueType.NameReference, index.GetTag("Text_ID")!.ValueType);
    }

    // ── GetAllTagDefinitions ────────────────────────────────────────────────

    [Fact]
    public void GetAllTagDefinitions_SameNameAcrossTypes_ReturnsAll()
    {
        var index = Build([
            ("TypeA", [Tag("Text_ID")]),
            ("TypeB", [Tag("Text_ID")]),
            ("TypeC", [Tag("Text_ID")])
        ]);
        Assert.Equal(3, index.GetAllTagDefinitions("Text_ID").Count);
    }

    [Fact]
    public void GetAllTagDefinitions_Unknown_ReturnsEmpty()
    {
        var index = Build([("Foo", [Tag("Health")])]);
        Assert.Empty(index.GetAllTagDefinitions("Missing"));
    }

    [Fact]
    public void GetAllTagDefinitions_CaseInsensitive()
    {
        var index = Build([("Foo", [Tag("Text_ID")])]);
        Assert.Single(index.GetAllTagDefinitions("text_id"));
    }

    // ── GetTagsForType ──────────────────────────────────────────────────────

    [Fact]
    public void GetTagsForType_ReturnsOnlyThatType()
    {
        var index = Build([
            ("TypeA", [Tag("Alpha"), Tag("Beta")]),
            ("TypeB", [Tag("Gamma")])
        ]);
        Assert.Equal(2, index.GetTagsForType("TypeA").Count);
        Assert.Single(index.GetTagsForType("TypeB"));
    }

    [Fact]
    public void GetTagsForType_CaseInsensitive()
    {
        var index = Build([("GameObjectType", [Tag("Health"), Tag("Speed")])]);
        Assert.Equal(2, index.GetTagsForType("gameobjecttype").Count);
        Assert.Equal(2, index.GetTagsForType("GAMEOBJECTTYPE").Count);
    }

    [Fact]
    public void GetTagsForType_Unknown_ReturnsEmpty()
    {
        var index = Build([("Foo", [Tag("Health")])]);
        Assert.Empty(index.GetTagsForType("DoesNotExist"));
    }

    // ── GetObjectType ───────────────────────────────────────────────────────

    [Fact]
    public void GetObjectType_ExistingType_ReturnsDefinition()
    {
        var index = Build([], [Type("GameConstants", null)]);
        Assert.Equal("GameConstants", index.GetObjectType("GameConstants")!.TypeName);
    }

    [Fact]
    public void GetObjectType_CaseInsensitive()
    {
        var index = Build([], [Type("GameObjectType")]);
        Assert.NotNull(index.GetObjectType("gameobjecttype"));
        Assert.NotNull(index.GetObjectType("GAMEOBJECTTYPE"));
    }

    [Fact]
    public void GetObjectType_Unknown_ReturnsNull()
    {
        var index = Build([], [Type("Faction")]);
        Assert.Null(index.GetObjectType("Missing"));
    }

    // ── AllTags / AllObjectTypes ────────────────────────────────────────────

    [Fact]
    public void AllTags_AggregatesDistinctFirstDefinitions()
    {
        // 2 types × 2 unique tags each = 4, but Text_ID appears in both → 3 distinct
        var index = Build([
            ("TypeA", [Tag("Health"), Tag("Text_ID")]),
            ("TypeB", [Tag("Speed"), Tag("Text_ID")])
        ]);
        Assert.Equal(3, index.AllTags.Count);
    }

    [Fact]
    public void AllObjectTypes_ReturnsAllTypes()
    {
        var index = Build([], [Type("A"), Type("B"), Type("C")]);
        Assert.Equal(3, index.AllObjectTypes.Count);
    }

    // ── Empty singleton ─────────────────────────────────────────────────────

    [Fact]
    public void Empty_HasNoTagsOrTypes()
    {
        Assert.Empty(SchemaIndex.Empty.AllTags);
        Assert.Empty(SchemaIndex.Empty.AllObjectTypes);
        Assert.Null(SchemaIndex.Empty.GetTag("Anything"));
        Assert.Null(SchemaIndex.Empty.GetObjectType("Anything"));
    }
}