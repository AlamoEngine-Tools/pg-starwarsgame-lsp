// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Parsing;

namespace PG.StarWarsGame.LSP.Xml.Tests.Parsing;

public sealed class XmlGameDocumentParserTest
{
    // ── helpers / fakes ──────────────────────────────────────────────────────

    private static XmlGameDocumentParser Build(FakeSchemaProvider? schema = null)
    {
        return new XmlGameDocumentParser(schema ?? new FakeSchemaProvider(),
            NullLogger<XmlGameDocumentParser>.Instance);
    }

    private static GameObjectTypeDefinition Type(string name, string? nameTag = "Name")
    {
        return new GameObjectTypeDefinition { TypeName = name, NameTag = nameTag };
    }

    private static XmlTagDefinition RefTag(string tag, string referenceType)
    {
        return new XmlTagDefinition
        {
            Tag = tag,
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceType = referenceType
        };
    }

    private static XmlTagDefinition ListRefTag(
        string tag, string referenceType,
        XmlValueType valueType = XmlValueType.GameObjectTypeReferenceList,
        TagSemanticType semanticType = TagSemanticType.Default)
    {
        return new XmlTagDefinition
        {
            Tag = tag,
            ValueType = valueType,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceType = referenceType,
            SemanticType = semanticType
        };
    }

    private static XmlTagDefinition PlainTag(string tag)
    {
        return new XmlTagDefinition { Tag = tag, ValueType = XmlValueType.Float };
    }

    // ── CanParse ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanParse_Returns_True_For_Xml()
    {
        Assert.True(Build().CanParse(".xml"));
        Assert.True(Build().CanParse(".XML"));
    }

    [Fact]
    public void CanParse_Returns_False_For_Non_Xml()
    {
        Assert.False(Build().CanParse(".lua"));
        Assert.False(Build().CanParse(".txt"));
        Assert.False(Build().CanParse(""));
    }

    // ── symbol extraction ────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_Known_Element_With_Name_Attribute_Emits_Symbol()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));

        var result = await Build(schema).ParseAsync(
            "file:///units.xml",
            """<Unit Name="UNIT_REBEL"><Max_Health>200</Max_Health></Unit>""",
            1, default);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("UNIT_REBEL", sym.Id);
        Assert.Equal(GameSymbolKind.XmlObject, sym.Kind);
        Assert.Equal("Unit", sym.TypeName);
        Assert.Equal("file:///units.xml", ((FileOrigin)sym.Origin).Uri);
    }

    [Fact]
    public async Task ParseAsync_Unknown_Element_Emits_No_Symbol()
    {
        var result = await Build().ParseAsync(
            "file:///f.xml",
            """<UnknownType Name="FOO"/>""",
            1, default);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_Singleton_Type_With_No_NameTag_Emits_No_Symbol()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("GameConstants", null)); // singleton: no Name attribute

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            "<GameConstants><Credits_Per_CP>50</Credits_Per_CP></GameConstants>",
            1, default);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_Element_With_Missing_Name_Attribute_Emits_No_Symbol()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            "<Unit><Max_Health>200</Max_Health></Unit>", // no Name attribute
            1, default);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_Multiple_Objects_Emits_Multiple_Symbols()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """
            <Unit Name="UNIT_A"><Max_Health>100</Max_Health></Unit>
            <Unit Name="UNIT_B"><Max_Health>200</Max_Health></Unit>
            """,
            1, default);

        Assert.Equal(2, result.Symbols.Length);
        Assert.Contains(result.Symbols, s => s.Id == "UNIT_A");
        Assert.Contains(result.Symbols, s => s.Id == "UNIT_B");
    }

    [Fact]
    public async Task ParseAsync_Symbol_TypeName_Matches_Schema_Type()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SFXEvent"));

        var result = await Build(schema).ParseAsync(
            "file:///sfx.xml",
            """<SFXEvent Name="SFX_LASER"/>""",
            1, default);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("SFXEvent", sym.TypeName);
    }

    // ── reference extraction ─────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_XmlObject_Reference_Tag_Emits_Reference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(RefTag("Spawn_Unit", "Unit"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="UNIT_A"><Spawn_Unit>UNIT_B</Spawn_Unit></Unit>""",
            1, default);

        var reference = Assert.Single(result.References);
        Assert.Equal("UNIT_B", reference.TargetId);
        Assert.Equal(GameSymbolKind.XmlObject, reference.ExpectedKind);
        Assert.Equal("Unit", reference.ExpectedTypeName);
        Assert.Equal("file:///f.xml", reference.DocumentUri);
    }

    [Fact]
    public async Task ParseAsync_Non_XmlObject_Reference_Tag_Emits_No_Reference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(PlainTag("Max_Health")); // plain float — not a reference

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="UNIT_A"><Max_Health>200</Max_Health></Unit>""",
            1, default);

        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_Reference_Has_Correct_Uri_And_Position()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(RefTag("Spawn_Unit", "Unit"));

        var result = await Build(schema).ParseAsync(
            "file:///units.xml",
            """<Unit Name="UNIT_A"><Spawn_Unit>UNIT_B</Spawn_Unit></Unit>""",
            1, default);

        var reference = Assert.Single(result.References);
        Assert.Equal("file:///units.xml", reference.DocumentUri);
    }

    // ── robustness ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_Malformed_Xml_Does_Not_Throw()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));

        // Unclosed tag — HtmlAgilityPack recovers gracefully
        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="UNIT_A"><Spawn_Unit>UNIT_B""",
            1, default);

        // May return partial results — must not throw
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ParseAsync_Empty_Document_Returns_Empty_Index()
    {
        var result = await Build().ParseAsync("file:///f.xml", "", 1, default);

        Assert.Empty(result.Symbols);
        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_Sets_DocumentUri_And_Version()
    {
        var result = await Build().ParseAsync("file:///f.xml", "<X/>", 7, default);

        Assert.Equal("file:///f.xml", result.DocumentUri);
        Assert.Equal(7, result.Version);
    }

    // ── multi-value reference splitting ──────────────────────────────────────

    [Fact]
    public async Task ParseAsync_CommaSeparated_ListTag_Emits_One_Reference_Per_Name()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Squadron"));
        schema.AddTag(ListRefTag("Squadron_Units", "Unit"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Squadron Name="SQ_1"><Squadron_Units>X_Wing, A_Wing, B_Wing</Squadron_Units></Squadron>""",
            1, default);

        Assert.Equal(3, result.References.Length);
        Assert.Contains(result.References, r => r.TargetId == "X_Wing");
        Assert.Contains(result.References, r => r.TargetId == "A_Wing");
        Assert.Contains(result.References, r => r.TargetId == "B_Wing");
    }

    [Fact]
    public async Task ParseAsync_SpaceSeparated_ListTag_Emits_One_Reference_Per_Name()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Squadron"));
        schema.AddTag(ListRefTag("Squadron_Units", "Unit"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Squadron Name="SQ_1"><Squadron_Units>X_Wing A_Wing B_Wing</Squadron_Units></Squadron>""",
            1, default);

        Assert.Equal(3, result.References.Length);
        Assert.Contains(result.References, r => r.TargetId == "X_Wing");
        Assert.Contains(result.References, r => r.TargetId == "A_Wing");
        Assert.Contains(result.References, r => r.TargetId == "B_Wing");
    }

    [Fact]
    public async Task ParseAsync_PerFactionObjectList_Skips_First_Token()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(ListRefTag("Transport_Units", "Unit", XmlValueType.PerFactionObjectList));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="UNIT_A"><Transport_Units>Pirates, Ship_A, Ship_B</Transport_Units></Unit>""",
            1, default);

        Assert.Equal(2, result.References.Length);
        Assert.DoesNotContain(result.References, r => r.TargetId == "Pirates");
        Assert.Contains(result.References, r => r.TargetId == "Ship_A");
        Assert.Contains(result.References, r => r.TargetId == "Ship_B");
    }

    [Fact]
    public async Task ParseAsync_PrerequisiteExpression_Emits_Reference_Per_Name()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(ListRefTag("Required_Special_Structures", "GameObjectType",
            XmlValueType.GameObjectTypeReferenceList, TagSemanticType.PrerequisiteExpression));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="UNIT_A"><Required_Special_Structures>A | B, C</Required_Special_Structures></Unit>""",
            1, default);

        Assert.Equal(3, result.References.Length);
        Assert.Contains(result.References, r => r.TargetId == "A");
        Assert.Contains(result.References, r => r.TargetId == "B");
        Assert.Contains(result.References, r => r.TargetId == "C");
    }

    [Fact]
    public async Task ParseAsync_Empty_ListTag_Value_Emits_No_References()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Squadron"));
        schema.AddTag(ListRefTag("Squadron_Units", "Unit"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Squadron Name="SQ_1"><Squadron_Units></Squadron_Units></Squadron>""",
            1, default);

        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_SingleValue_RefTag_Still_Emits_Exactly_One_Reference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(RefTag("Spawn_Unit", "Unit"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="UNIT_A"><Spawn_Unit>UNIT_B</Spawn_Unit></Unit>""",
            1, default);

        var reference = Assert.Single(result.References);
        Assert.Equal("UNIT_B", reference.TargetId);
    }

    // ── FakeSchemaProvider ───────────────────────────────────────────────────

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, GameObjectTypeDefinition> _types =
            new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? GetTag(string name)
        {
            return _tags.GetValueOrDefault(name);
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [.. _tags.Values];

        public GameObjectTypeDefinition? GetObjectType(string name)
        {
            return _types.GetValueOrDefault(name);
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [.. _types.Values];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string _)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];

        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public void AddTag(XmlTagDefinition tag)
        {
            _tags[tag.Tag] = tag;
        }

        public void AddType(GameObjectTypeDefinition type)
        {
            _types[type.TypeName] = type;
        }
    }
}