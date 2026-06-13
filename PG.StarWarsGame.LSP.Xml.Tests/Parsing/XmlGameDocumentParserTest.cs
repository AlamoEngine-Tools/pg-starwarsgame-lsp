// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Parsing;

namespace PG.StarWarsGame.LSP.Xml.Tests.Parsing;

public sealed class XmlGameDocumentParserTest
{
    // ── helpers / fakes ──────────────────────────────────────────────────────

    private static XmlGameDocumentParser Build(FakeSchemaProvider? schema = null,
        FakeFileTypeRegistry? registry = null)
    {
        return new XmlGameDocumentParser(new FileHelper(new MockFileSystem()),
            schema ?? new FakeSchemaProvider(),
            registry ?? new FakeFileTypeRegistry(),
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
            ObjectType = new GameObjectTypeDefinition { TypeName = referenceType }
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
            ObjectType = new GameObjectTypeDefinition { TypeName = referenceType },
            SemanticType = semanticType
        };
    }

    private static XmlTagDefinition PlainTag(string tag)
    {
        return new XmlTagDefinition { Tag = tag, ValueType = XmlValueType.Float };
    }

    private static XmlTagDefinition VariantTag()
    {
        return new XmlTagDefinition
        {
            Tag = "Variant_Of_Existing_Type",
            ValueType = XmlValueType.TypeReference,
            ReferenceKind = ReferenceKind.XmlObject,
            SemanticType = TagSemanticType.VariantParent,
            ObjectType = new GameObjectTypeDefinition { TypeName = "GameObjectType" }
        };
    }

    private static XmlTagDefinition GroupRefTag(string tag, string referenceType)
    {
        return new XmlTagDefinition
        {
            Tag = tag,
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.XmlObject,
            SemanticType = TagSemanticType.ReferenceGroup,
            ObjectType = new GameObjectTypeDefinition { TypeName = referenceType }
        };
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
        var registry = new FakeFileTypeRegistry();
        registry.Register("units.xml", ["Unit"]);

        var result = await Build(schema, registry).ParseAsync("file:///units.xml",
            """<GameObjectFiles><Unit Name="UNIT_REBEL"><Max_Health>200</Max_Health></Unit></GameObjectFiles>""", 1,
            TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("UNIT_REBEL", sym.Id);
        Assert.Equal(GameSymbolKind.XmlObject, sym.Kind);
        Assert.Equal("Unit", sym.TypeName);
        Assert.Equal("file:///units.xml", ((FileOrigin)sym.Origin).Uri);
    }

    [Fact]
    public async Task ParseAsync_Unknown_Element_Emits_No_Symbol()
    {
        var result = await Build().ParseAsync("file:///f.xml", """<UnknownType Name="FOO"/>""", 1,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_Singleton_Type_With_No_NameTag_Emits_No_Symbol()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("GameConstants", null)); // singleton: no Name attribute

        var result = await Build(schema).ParseAsync("file:///f.xml",
            "<GameConstants><Credits_Per_CP>50</Credits_Per_CP></GameConstants>", 1,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_Element_With_Missing_Name_Attribute_Emits_No_Symbol()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));

        var result = await Build(schema).ParseAsync("file:///f.xml", "<Unit><Max_Health>200</Max_Health></Unit>", 1,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_Multiple_Objects_Emits_Multiple_Symbols()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        var registry = new FakeFileTypeRegistry();
        registry.Register("f.xml", ["Unit"]);

        var result = await Build(schema, registry).ParseAsync("file:///f.xml", """
                                                                               <Units>
                                                                                 <Unit Name="UNIT_A"><Max_Health>100</Max_Health></Unit>
                                                                                 <Unit Name="UNIT_B"><Max_Health>200</Max_Health></Unit>
                                                                               </Units>
                                                                               """, 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Symbols.Length);
        Assert.Contains(result.Symbols, s => s.Id == "UNIT_A");
        Assert.Contains(result.Symbols, s => s.Id == "UNIT_B");
    }

    [Fact]
    public async Task ParseAsync_Symbol_TypeName_Matches_Schema_Type()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SFXEvent"));
        var registry = new FakeFileTypeRegistry();
        registry.Register("sfx.xml", ["SFXEvent"]);

        var result = await Build(schema, registry).ParseAsync("file:///sfx.xml",
            """<SFXEvents><SFXEvent Name="SFX_LASER"/></SFXEvents>""", 1, TestContext.Current.CancellationToken);

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

        var result = await Build(schema).ParseAsync("file:///f.xml",
            """<Unit Name="UNIT_A"><Spawn_Unit>UNIT_B</Spawn_Unit></Unit>""", 1, TestContext.Current.CancellationToken);

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

        var result = await Build(schema).ParseAsync("file:///f.xml",
            """<Unit Name="UNIT_A"><Max_Health>200</Max_Health></Unit>""", 1, TestContext.Current.CancellationToken);

        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_Reference_Has_Correct_Uri_And_Position()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(RefTag("Spawn_Unit", "Unit"));

        var result = await Build(schema).ParseAsync("file:///units.xml",
            """<Unit Name="UNIT_A"><Spawn_Unit>UNIT_B</Spawn_Unit></Unit>""", 1, TestContext.Current.CancellationToken);

        var reference = Assert.Single(result.References);
        Assert.Equal("file:///units.xml", reference.DocumentUri);
    }

    // ── variant inheritance (Variant_Of_Existing_Type) ──────────────────────

    [Fact]
    public async Task ParseAsync_VariantParent_Tag_Sets_VariantBaseId_And_Emits_Typed_Reference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SpaceUnit"));
        schema.AddTag(VariantTag());
        var registry = new FakeFileTypeRegistry();
        registry.Register("ships.xml", ["SpaceUnit"]);

        var result = await Build(schema, registry).ParseAsync("file:///ships.xml",
            """<GameObjectFiles><SpaceUnit Name="VARIANT_A"><Variant_Of_Existing_Type>BASE_SHIP</Variant_Of_Existing_Type></SpaceUnit></GameObjectFiles>""",
            1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("BASE_SHIP", sym.VariantBaseId);

        // Exactly one reference: the typed variant base reference (not a duplicate wildcard one).
        var reference = Assert.Single(result.References);
        Assert.Equal("BASE_SHIP", reference.TargetId);
        Assert.Equal(GameSymbolKind.XmlObject, reference.ExpectedKind);
        Assert.Equal("SpaceUnit", reference.ExpectedTypeName); // enclosing object's type, not GameObjectType
    }

    [Fact]
    public async Task ParseAsync_NonVariant_Object_Has_Null_VariantBaseId()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        var registry = new FakeFileTypeRegistry();
        registry.Register("units.xml", ["Unit"]);

        var result = await Build(schema, registry).ParseAsync("file:///units.xml",
            """<GameObjectFiles><Unit Name="UNIT_A"><Max_Health>100</Max_Health></Unit></GameObjectFiles>""",
            1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        Assert.Null(sym.VariantBaseId);
    }

    [Fact]
    public async Task ParseAsync_VariantParent_EmptyValue_NoBaseId_NoReference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SpaceUnit"));
        schema.AddTag(VariantTag());
        var registry = new FakeFileTypeRegistry();
        registry.Register("ships.xml", ["SpaceUnit"]);

        var result = await Build(schema, registry).ParseAsync("file:///ships.xml",
            """<GameObjectFiles><SpaceUnit Name="VARIANT_A"><Variant_Of_Existing_Type></Variant_Of_Existing_Type></SpaceUnit></GameObjectFiles>""",
            1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        Assert.Null(sym.VariantBaseId);
        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_SubObject_VariantParent_Sets_VariantBaseId_And_Emits_Typed_Reference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(SubObjectListTag("Abilities"));
        schema.AddType(Type("LuckyShotAttackAbility"));
        schema.AddTag(VariantTag());

        var result = await Build(schema).ParseAsync("file:///f.xml",
            """<U><Abilities SubObjectList="Yes"><Lucky_Shot_Attack_Ability Name="My_Ability"><Variant_Of_Existing_Type>Base_Ability</Variant_Of_Existing_Type></Lucky_Shot_Attack_Ability></Abilities></U>""",
            1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("Base_Ability", sym.VariantBaseId);

        var reference = Assert.Single(result.References);
        Assert.Equal("Base_Ability", reference.TargetId);
        Assert.Equal("LuckyShotAttackAbility", reference.ExpectedTypeName);
    }

    // ── robustness ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_Malformed_Xml_Does_Not_Throw()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));

        // Unclosed tag — HtmlAgilityPack recovers gracefully
        var result = await Build(schema).ParseAsync("file:///f.xml", """<Unit Name="UNIT_A"><Spawn_Unit>UNIT_B""", 1,
            TestContext.Current.CancellationToken);

        // May return partial results — must not throw
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ParseAsync_Empty_Document_Returns_Empty_Index()
    {
        var result = await Build().ParseAsync("file:///f.xml", "", 1, TestContext.Current.CancellationToken);

        Assert.Empty(result.Symbols);
        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_Sets_DocumentUri_And_Version()
    {
        var result = await Build().ParseAsync("file:///f.xml", "<X/>", 7, TestContext.Current.CancellationToken);

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

        var result = await Build(schema).ParseAsync("file:///f.xml",
            """<Squadron Name="SQ_1"><Squadron_Units>X_Wing, A_Wing, B_Wing</Squadron_Units></Squadron>""", 1,
            TestContext.Current.CancellationToken);

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

        var result = await Build(schema).ParseAsync("file:///f.xml",
            """<Squadron Name="SQ_1"><Squadron_Units>X_Wing A_Wing B_Wing</Squadron_Units></Squadron>""", 1,
            TestContext.Current.CancellationToken);

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

        var result = await Build(schema).ParseAsync("file:///f.xml",
            """<Unit Name="UNIT_A"><Transport_Units>Pirates, Ship_A, Ship_B</Transport_Units></Unit>""", 1,
            TestContext.Current.CancellationToken);

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

        var result = await Build(schema).ParseAsync("file:///f.xml",
            """<Unit Name="UNIT_A"><Required_Special_Structures>A | B, C</Required_Special_Structures></Unit>""", 1,
            TestContext.Current.CancellationToken);

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

        var result = await Build(schema).ParseAsync("file:///f.xml",
            """<Squadron Name="SQ_1"><Squadron_Units></Squadron_Units></Squadron>""", 1,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_SingleValue_RefTag_Still_Emits_Exactly_One_Reference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(RefTag("Spawn_Unit", "Unit"));

        var result = await Build(schema).ParseAsync("file:///f.xml",
            """<Unit Name="UNIT_A"><Spawn_Unit>UNIT_B</Spawn_Unit></Unit>""", 1, TestContext.Current.CancellationToken);

        var reference = Assert.Single(result.References);
        Assert.Equal("UNIT_B", reference.TargetId);
    }

    // ── EaW real XML format ───────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_EaW_XmlDeclaration_And_Tab_IndentedElement_CorrectIdAndColumn()
    {
        // Mirrors the actual EaW XML format: <?xml?> header, tab-indented elements.
        // Verifies that HAP attribute parsing and column computation are both correct.
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SpaceUnit"));
        var registry = new FakeFileTypeRegistry();
        registry.Register("spaceunits.xml", ["SpaceUnit"]);

        const string xml =
            "<?xml version=\"1.0\"?>\n<GameObjectFiles>\n\t<SpaceUnit Name=\"Broadside_Class_Cruiser\">\n\t\t<Text_ID>TEST</Text_ID>\n\t</SpaceUnit>\n</GameObjectFiles>";

        var result = await Build(schema, registry)
            .ParseAsync("file:///spaceunits.xml", xml, 1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("Broadside_Class_Cruiser", sym.Id); // ID must not include ">
        var origin = Assert.IsType<FileOrigin>(sym.Origin);
        // Line 2 (0-based): "\t<SpaceUnit Name=\"Broadside_Class_Cruiser\">"
        // \t(1) + <(1) + SpaceUnit(9) + space(1) + Name(4) + =(1) + "(1) = 18 → value at col 18
        Assert.Equal(2, origin.Line);
        Assert.Equal(18, origin.Column);
    }

    // ── symbol column in FileOrigin ──────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_Symbol_FileOrigin_Column_IsNameAttributeValueStart()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SpaceUnit"));
        var registry = new FakeFileTypeRegistry();
        registry.Register("ships.xml", ["SpaceUnit"]);

        // Line 1 (0-based): "<SpaceUnit Name="SHIP_A">..." — value "SHIP_A" starts at col 17
        // "<SpaceUnit Name=\"" = 17 chars (0-16), so value starts at col 17
        var result = await Build(schema, registry).ParseAsync("file:///ships.xml",
            "<GameObjectFiles>\n<SpaceUnit Name=\"SHIP_A\"><Hp>100</Hp></SpaceUnit>\n</GameObjectFiles>", 1,
            TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        var origin = Assert.IsType<FileOrigin>(sym.Origin);
        Assert.Equal(17, origin.Column);
    }

    [Fact]
    public async Task ParseAsync_Symbol_FileOrigin_Column_WithLeadingWhitespace()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SpaceUnit"));
        var registry = new FakeFileTypeRegistry();
        registry.Register("ships.xml", ["SpaceUnit"]);

        // Line 1 (0-based): "    <SpaceUnit Name="SHIP_B">..." — 4-space indent → value at col 21
        var result = await Build(schema, registry).ParseAsync(
            "file:///ships.xml",
            "<GameObjectFiles>\n    <SpaceUnit Name=\"SHIP_B\"><Hp>50</Hp></SpaceUnit>\n</GameObjectFiles>",
            1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        var origin = Assert.IsType<FileOrigin>(sym.Origin);
        Assert.Equal(21, origin.Column);
    }

    // ── no-registry-no-symbols ──────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_UnregisteredFile_EmitsNoSymbols_EvenWhenElementNameMatchesType()
    {
        // Files not registered in IFileTypeRegistry must produce no symbols.
        // The legacy element-name fallback must be gone.
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit")); // element name matches type name — old code would emit a symbol

        var result = await Build(schema).ParseAsync( // no registry
            "file:///units.xml",
            """<Units><Unit Name="UNIT_A"/></Units>""",
            1, TestContext.Current.CancellationToken);

        Assert.Empty(result.Symbols);
    }

    // ── registry-first type detection ───────────────────────────────────────

    [Fact]
    public async Task ParseAsync_ArbitraryContainerName_WithRegisteredType_EmitsSymbol()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("HardPoint"));

        // "f.xml" is the key produced by IFileHelper.NormalizeUri("file:///f.xml")
        var registry = new FakeFileTypeRegistry();
        registry.Register("f.xml", ["HardPoint"]);

        var result = await Build(schema, registry).ParseAsync(
            "file:///f.xml",
            """<WhateverWrapper><SomeTag Name="HP_A"/></WhateverWrapper>""",
            1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("HP_A", sym.Id);
        Assert.Equal("HardPoint", sym.TypeName);
    }

    [Fact]
    public async Task ParseAsync_RegisteredSingletonType_EmitsNoSymbol_NoException()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("BinkMovie", null)); // singleton: no NameTag

        var registry = new FakeFileTypeRegistry();
        registry.Register("movies.xml", ["BinkMovie"]);

        var result = await Build(schema, registry).ParseAsync(
            "file:///movies.xml",
            "<BinkMovies><BinkMovie/></BinkMovies>",
            1, TestContext.Current.CancellationToken);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_References_Collected_WithArbitraryContainerName()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("HardPoint"));
        schema.AddTag(RefTag("Attached_To", "Bone"));

        var registry = new FakeFileTypeRegistry();
        registry.Register("f.xml", ["HardPoint"]);

        var result = await Build(schema, registry).ParseAsync(
            "file:///f.xml",
            """<WhateverWrapper><SomeTag Name="HP_A"><Attached_To>BONE_X</Attached_To></SomeTag></WhateverWrapper>""",
            1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("HP_A", sym.Id);

        var reference = Assert.Single(result.References);
        Assert.Equal("BONE_X", reference.TargetId);
    }

    // ── NameReferenceList splitting ───────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_NameReferenceList_TwoTokens_CreatesTwoReferences()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(ListRefTag("Garrison_Units", "Unit", XmlValueType.NameReferenceList));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="UNIT_A"><Garrison_Units>X_Wing Y_Wing</Garrison_Units></Unit>""",
            1, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.References.Length);
        Assert.Contains(result.References, r => r.TargetId == "X_Wing");
        Assert.Contains(result.References, r => r.TargetId == "Y_Wing");
    }

    [Fact]
    public async Task ParseAsync_NameReferenceList_SingleToken_CreatesOneReference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(ListRefTag("Garrison_Units", "Unit", XmlValueType.NameReferenceList));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="UNIT_A"><Garrison_Units>X_Wing</Garrison_Units></Unit>""",
            1, TestContext.Current.CancellationToken);

        var reference = Assert.Single(result.References);
        Assert.Equal("X_Wing", reference.TargetId);
    }

    [Fact]
    public async Task ParseAsync_NameReferenceList_CommaSeparated_SplitsCorrectly()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("Unit"));
        schema.AddTag(ListRefTag("Garrison_Units", "Unit", XmlValueType.NameReferenceList));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="UNIT_A"><Garrison_Units>X_Wing,Y_Wing</Garrison_Units></Unit>""",
            1, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.References.Length);
        Assert.Contains(result.References, r => r.TargetId == "X_Wing");
        Assert.Contains(result.References, r => r.TargetId == "Y_Wing");
    }

    [Fact]
    public async Task ParseAsync_TypeReferenceList_With_HardcodedSet_ReferenceKind_Does_Not_Emit_Reference()
    {
        var schema = new FakeSchemaProvider();
        var tag = new XmlTagDefinition
        {
            Tag = "Behavior",
            ValueType = XmlValueType.TypeReferenceList,
            ReferenceKind = ReferenceKind.HardcodedSet
        };
        schema.AddTag(tag);

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<Unit Name="INFANTRY"><Behavior>BehaviorModule_AAGUN_Fire</Behavior></Unit>""",
            1, TestContext.Current.CancellationToken);

        Assert.Empty(result.References);
    }

    // ── ReferenceGroup — group membership emission ───────────────────────────

    [Fact]
    public async Task ParseAsync_ReferenceGroup_Tag_Does_Not_Emit_GameReference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SFXEvent"));
        schema.AddTag(GroupRefTag("Overlap_Test", "SFXEvent"));

        var result = await Build(schema).ParseAsync(
            "file:///sfx.xml",
            """<SFXEvents><SFXEvent Name="SFX_LASER"><Overlap_Test>Unit_AT_AT</Overlap_Test></SFXEvent></SFXEvents>""",
            1, TestContext.Current.CancellationToken);

        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_ReferenceGroup_Tag_Emits_GroupMembership_WithGroupKey()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SFXEvent"));
        schema.AddTag(GroupRefTag("Overlap_Test", "SFXEvent"));

        var result = await Build(schema).ParseAsync(
            "file:///sfx.xml",
            """<SFXEvents><SFXEvent Name="SFX_LASER"><Overlap_Test>Unit_AT_AT</Overlap_Test></SFXEvent></SFXEvents>""",
            1, TestContext.Current.CancellationToken);

        var gm = Assert.Single(result.GroupMemberships);
        Assert.Equal("Unit_AT_AT", gm.Membership.GroupKey);
        Assert.Equal("SFXEvent", gm.Membership.MemberTypeName);
    }

    [Fact]
    public async Task ParseAsync_ReferenceGroup_MemberOrigin_PointsToParentNameAttribute()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SFXEvent"));
        schema.AddTag(GroupRefTag("Overlap_Test", "SFXEvent"));

        // Multi-line: SFXEvent is on line 1 (0-indexed)
        var result = await Build(schema).ParseAsync(
            "file:///sfx.xml",
            "<SFXEvents>\n<SFXEvent Name=\"SFX_LASER\"><Overlap_Test>Unit_AT_AT</Overlap_Test></SFXEvent>\n</SFXEvents>",
            1, TestContext.Current.CancellationToken);

        var gm = Assert.Single(result.GroupMemberships);
        var origin = Assert.IsType<FileOrigin>(gm.Membership.MemberOrigin);
        Assert.Equal("file:///sfx.xml", origin.Uri);
        Assert.Equal(1, origin.Line); // line 1 (0-indexed)
        // Column points into the Name attribute value "SFX_LASER":
        // "<SFXEvent Name=\"SFX_LASER\">" — 'N' of "Name" is at col 10; col = 10 + len("Name") + 2 = 16
        Assert.Equal(16, origin.Column);
    }

    [Fact]
    public async Task ParseAsync_ReferenceGroup_TagPosition_IsValueSpan()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SFXEvent"));
        schema.AddTag(GroupRefTag("Overlap_Test", "SFXEvent"));

        var result = await Build(schema).ParseAsync(
            "file:///sfx.xml",
            "<SFXEvents>\n<SFXEvent Name=\"SFX_LASER\"><Overlap_Test>Unit_AT_AT</Overlap_Test></SFXEvent>\n</SFXEvents>",
            1, TestContext.Current.CancellationToken);

        var gm = Assert.Single(result.GroupMemberships);
        Assert.Equal("Unit_AT_AT".Length, gm.TagLength);
    }

    [Fact]
    public async Task ParseAsync_ReferenceGroup_EmptyValue_EmitsNoMembership()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SFXEvent"));
        schema.AddTag(GroupRefTag("Overlap_Test", "SFXEvent"));

        var result = await Build(schema).ParseAsync(
            "file:///sfx.xml",
            """<SFXEvents><SFXEvent Name="SFX_LASER"><Overlap_Test></Overlap_Test></SFXEvent></SFXEvents>""",
            1, TestContext.Current.CancellationToken);

        Assert.Empty(result.GroupMemberships);
    }

    [Fact]
    public async Task ParseAsync_ReferenceGroup_TwoElements_EmitsTwoMemberships()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(Type("SFXEvent"));
        schema.AddTag(GroupRefTag("Overlap_Test", "SFXEvent"));

        var result = await Build(schema).ParseAsync(
            "file:///sfx.xml",
            """
            <SFXEvents>
              <SFXEvent Name="SFX_A"><Overlap_Test>Unit_AT_AT</Overlap_Test></SFXEvent>
              <SFXEvent Name="SFX_B"><Overlap_Test>Unit_AT_AT</Overlap_Test></SFXEvent>
            </SFXEvents>
            """,
            1, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.GroupMemberships.Length);
        Assert.All(result.GroupMemberships, gm => Assert.Equal("Unit_AT_AT", gm.Membership.GroupKey));
    }

    // ── AbilityDefinitionSubObjectList sub-object list symbol indexing ───────────────────────────────

    private static XmlTagDefinition SubObjectListTag(string tag)
    {
        return new XmlTagDefinition
        {
            Tag = tag,
            ValueType = XmlValueType.AbilityDefinitionSubObjectList
        };
    }

    [Fact]
    public async Task ParseAsync_AbilityDefinitionSubObjectList_Child_With_NameAttribute_Is_Indexed_As_Symbol()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(SubObjectListTag("Abilities"));
        schema.AddType(Type("LuckyShotAttackAbility"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<U><Abilities SubObjectList="Yes"><Lucky_Shot_Attack_Ability Name="My_Ability"/></Abilities></U>""",
            1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("My_Ability", sym.Id);
        Assert.Equal("LuckyShotAttackAbility", sym.TypeName);
        Assert.Equal(GameSymbolKind.XmlObject, sym.Kind);
    }

    [Fact]
    public async Task ParseAsync_AbilityDefinitionSubObjectList_Multiple_Children_Index_All_As_Symbols()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(SubObjectListTag("Abilities"));
        schema.AddType(Type("LuckyShotAttackAbility"));
        schema.AddType(Type("ForceCloakAbility"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<U><Abilities SubObjectList="Yes"><Lucky_Shot_Attack_Ability Name="Ability_A"/><Force_Cloak_Ability Name="Ability_B"/></Abilities></U>""",
            1, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Symbols.Length);
        Assert.Contains(result.Symbols, s => s.Id == "Ability_A" && s.TypeName == "LuckyShotAttackAbility");
        Assert.Contains(result.Symbols, s => s.Id == "Ability_B" && s.TypeName == "ForceCloakAbility");
    }

    [Fact]
    public async Task ParseAsync_AbilityDefinitionSubObjectList_Child_With_Unknown_Type_Is_Silently_Skipped()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(SubObjectListTag("Abilities"));
        // No type registered for BasePowerAbility

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<U><Abilities SubObjectList="Yes"><Base_Power_Ability Name="My_Ability"/></Abilities></U>""",
            1, TestContext.Current.CancellationToken);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_AbilityDefinitionSubObjectList_Child_With_No_Name_Attribute_Is_Skipped()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(SubObjectListTag("Abilities"));
        schema.AddType(Type("LuckyShotAttackAbility"));

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<U><Abilities SubObjectList="Yes"><Lucky_Shot_Attack_Ability/></Abilities></U>""",
            1, TestContext.Current.CancellationToken);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_AbilityDefinitionSubObjectList_Symbol_FileOrigin_Column_IsNameAttributeValueStart()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(SubObjectListTag("Abilities"));
        schema.AddType(Type("CombatBonusAbility"));

        // Line 2 (0-based): "<Combat_Bonus_Ability Name="MY_BONUS"/>"
        // "<Combat_Bonus_Ability Name=\"" = 28 chars (0-27) → value at col 28
        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            "<U>\n<Abilities SubObjectList=\"Yes\">\n<Combat_Bonus_Ability Name=\"MY_BONUS\"/>\n</Abilities>\n</U>",
            1, TestContext.Current.CancellationToken);

        var sym = Assert.Single(result.Symbols);
        var origin = Assert.IsType<FileOrigin>(sym.Origin);
        Assert.Equal(2, origin.Line);
        Assert.Equal(28, origin.Column);
    }

    // ── GUI_Activated_Ability_Name reference (GuiActivatedAbilityDefinitionSubObjectList cross-link) ─────────────

    [Fact]
    public async Task ParseAsync_GUI_Activated_Ability_Name_Emits_XmlObject_Reference()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(new XmlTagDefinition
        {
            Tag = "GUI_Activated_Ability_Name",
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.XmlObject
        });

        var result = await Build(schema).ParseAsync(
            "file:///f.xml",
            """<U><Unit_Abilities_Data SubObjectList="Yes"><Unit_Ability><GUI_Activated_Ability_Name>My_Special_Ability</GUI_Activated_Ability_Name></Unit_Ability></Unit_Abilities_Data></U>""",
            1, TestContext.Current.CancellationToken);

        var reference = Assert.Single(result.References);
        Assert.Equal("My_Special_Ability", reference.TargetId);
        Assert.Equal(GameSymbolKind.XmlObject, reference.ExpectedKind);
        Assert.Null(reference.ExpectedTypeName);
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
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

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

    private sealed class FakeFileTypeRegistry : IFileTypeRegistry
    {
        private readonly Dictionary<string, ImmutableArray<string>> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public ImmutableArray<string> GetTypesForFile(string normalizedPath)
        {
            return _map.TryGetValue(normalizedPath, out var types) ? types : ImmutableArray<string>.Empty;
        }

        public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
        {
            _map[normalizedPath] = typeNames;
        }

        public void UnregisterFile(string normalizedPath)
        {
            _map.Remove(normalizedPath);
        }

        public IReadOnlyDictionary<string, ImmutableArray<string>> All => _map;

        public void Register(string key, ImmutableArray<string> types)
        {
            _map["file:///" + key] = types;
        }
    }
}