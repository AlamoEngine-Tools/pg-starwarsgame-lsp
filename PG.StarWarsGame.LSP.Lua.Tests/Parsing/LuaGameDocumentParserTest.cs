// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Parsing;

public sealed class LuaGameDocumentParserTest
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static LuaApiSchemaProvider BuildSchema()
    {
        return new LuaApiSchemaProvider([
            """
            ---@param typeName string
            ---@xmlref XmlObject
            function Find_First_Object(typeName) end
            ---@param typeName string
            ---@xmlref XmlObject
            function Find_Object_Type(typeName) end
            ---@param typeName string
            ---@xmlref XmlObject
            function Find_All_Objects_Of_Type(typeName) end
            ---@param playerName string
            ---@xmlref XmlObject:Faction
            function Find_Player(playerName) end
            """
        ]);
    }

    private static LuaGameDocumentParser Build(LuaAnnotationRepository? repo = null)
    {
        return new LuaGameDocumentParser(BuildSchema(),
            new FileHelper(new MockFileSystem()),
            NullLogger<LuaGameDocumentParser>.Instance,
            repo ?? new LuaAnnotationRepository());
    }

    // ── CanParse ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanParse_Returns_True_For_Lua()
    {
        Assert.True(Build().CanParse(".lua"));
        Assert.True(Build().CanParse(".LUA"));
        Assert.True(Build().CanParse(".Lua"));
    }

    [Fact]
    public void CanParse_Returns_False_For_Non_Lua()
    {
        Assert.False(Build().CanParse(".xml"));
        Assert.False(Build().CanParse(".txt"));
        Assert.False(Build().CanParse(""));
        Assert.False(Build().CanParse(".luac"));
    }

    // ── symbol extraction ────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_GlobalFunction_EmitsLuaGlobalSymbol()
    {
        var result = await Build().ParseAsync(
            "file:///script.lua",
            "function Definitions() end",
            1, default);

        var sym = Assert.Single(result.Symbols);
        Assert.Equal("Definitions", sym.Id);
        Assert.Equal(GameSymbolKind.LuaGlobal, sym.Kind);
        Assert.Null(sym.TypeName);
        Assert.Equal("file:///script.lua", ((FileOrigin)sym.Origin).Uri);
    }

    [Fact]
    public async Task ParseAsync_MultipleGlobalFunctions_EmitsOneSymbolEach()
    {
        var result = await Build().ParseAsync(
            "file:///s.lua",
            """
            function Definitions() end
            function State_Init(message) end
            function main() end
            """,
            1, default);

        Assert.Equal(3, result.Symbols.Length);
        Assert.Contains(result.Symbols, s => s.Id == "Definitions");
        Assert.Contains(result.Symbols, s => s.Id == "State_Init");
        Assert.Contains(result.Symbols, s => s.Id == "main");
    }

    [Fact]
    public async Task ParseAsync_LocalFunction_EmitsNoSymbol()
    {
        var result = await Build().ParseAsync(
            "file:///s.lua",
            "local function Helper() end",
            1, default);

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ParseAsync_EmptyFile_ReturnsEmptyIndex()
    {
        var result = await Build().ParseAsync("file:///s.lua", "", 1, default);

        Assert.Empty(result.Symbols);
        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_Symbol_Origin_RecordsLineNumber()
    {
        const string text = """

                            function Definitions() end
                            """;

        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);

        var sym = Assert.Single(result.Symbols);
        var origin = (FileOrigin)sym.Origin;
        Assert.Equal(1, origin.Line); // 0-based; function is on line index 1
    }

    [Fact]
    public async Task ParseAsync_Symbol_Origin_ColumnPointsToFunctionName_NotKeyword()
    {
        // "function Foo()" — 'F' is at column 9 ("function " = 9 chars)
        var result = await Build().ParseAsync("file:///s.lua", "function Foo() end", 1, default);

        var sym = Assert.Single(result.Symbols);
        var origin = (FileOrigin)sym.Origin;
        Assert.Equal(9, origin.Column); // after "function " prefix
    }

    [Fact]
    public async Task ParseAsync_Symbol_Origin_Column_IndentedFunction()
    {
        // "  function Bar() end" — 'B' is at column 11 ("  function " = 11 chars)
        var result = await Build().ParseAsync("file:///s.lua", "  function Bar() end", 1, default);

        var sym = Assert.Single(result.Symbols);
        var origin = (FileOrigin)sym.Origin;
        Assert.Equal(11, origin.Column); // after "  function " prefix
    }

    [Fact]
    public async Task ParseAsync_Sets_DocumentUri_And_Version()
    {
        var result = await Build().ParseAsync("file:///s.lua", "", 7, default);

        Assert.Equal("file:///s.lua", result.DocumentUri);
        Assert.Equal(7, result.Version);
    }

    // ── XML reference extraction ─────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_Find_First_Object_Call_EmitsXmlObjectReference()
    {
        var result = await Build().ParseAsync(
            "file:///s.lua",
            """Find_First_Object("UNIT_REBEL")""",
            1, default);

        var reference = Assert.Single(result.References);
        Assert.Equal("UNIT_REBEL", reference.TargetId);
        Assert.Equal(GameSymbolKind.XmlObject, reference.ExpectedKind);
        Assert.Null(reference.ExpectedTypeName);
        Assert.Equal("file:///s.lua", reference.DocumentUri);
    }

    [Fact]
    public async Task ParseAsync_Find_Player_Call_EmitsXmlObjectReference_WithFactionType()
    {
        var result = await Build().ParseAsync(
            "file:///s.lua",
            """Find_Player("Rebel")""",
            1, default);

        var reference = Assert.Single(result.References);
        Assert.Equal("Rebel", reference.TargetId);
        Assert.Equal(GameSymbolKind.XmlObject, reference.ExpectedKind);
        Assert.Equal("Faction", reference.ExpectedTypeName);
    }

    [Fact]
    public async Task ParseAsync_Find_Object_Type_Call_EmitsXmlObjectReference()
    {
        var result = await Build().ParseAsync(
            "file:///s.lua",
            """Find_Object_Type("UNIT_HEAVY_TANK")""",
            1, default);

        var reference = Assert.Single(result.References);
        Assert.Equal("UNIT_HEAVY_TANK", reference.TargetId);
        Assert.Equal(GameSymbolKind.XmlObject, reference.ExpectedKind);
    }

    [Fact]
    public async Task ParseAsync_UnknownXmlApiFunction_EmitsLuaGlobalCallReference()
    {
        // SomeRandomFunction is not in the EaW API schema, so no XML ref is emitted for its
        // argument; instead, the callee itself is tracked as a LuaGlobal call reference so
        // rename can locate all call sites via the index.
        var result = await Build().ParseAsync(
            "file:///s.lua",
            """SomeRandomFunction("UNIT_REBEL")""",
            1, default);

        var reference = Assert.Single(result.References);
        Assert.Equal("SomeRandomFunction", reference.TargetId);
        Assert.Equal(GameSymbolKind.LuaGlobal, reference.ExpectedKind);
        Assert.Null(reference.ExpectedTypeName);
    }

    [Fact]
    public async Task ParseAsync_FunctionCallCallee_EmitsLuaGlobalReference()
    {
        var result = await Build().ParseAsync(
            "file:///s.lua",
            "RunMission()",
            1, default);

        var reference = Assert.Single(result.References);
        Assert.Equal("RunMission", reference.TargetId);
        Assert.Equal(GameSymbolKind.LuaGlobal, reference.ExpectedKind);
        Assert.Null(reference.ExpectedTypeName);
        Assert.Equal("file:///s.lua", reference.DocumentUri);
        Assert.Equal(0, reference.Line);
        Assert.Equal(0, reference.Column);
        Assert.Equal("RunMission".Length, reference.Length);
    }

    [Fact]
    public async Task ParseAsync_KnownFunction_WithVariableArg_EmitsNoReference()
    {
        var result = await Build().ParseAsync(
            "file:///s.lua",
            """Find_First_Object(unit_name)""",
            1, default);

        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ParseAsync_Reference_HasCorrect_LineAndColumn()
    {
        const string text = """
                            Find_First_Object("UNIT_REBEL")
                            """;

        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);

        var reference = Assert.Single(result.References);
        Assert.Equal(0, reference.Line); // 0-based, first line
        Assert.Equal(19, reference.Column); // 0-based column of U in UNIT_REBEL (after opening quote at col 18)
        Assert.Equal("UNIT_REBEL".Length, reference.Length);
    }

    [Fact]
    public async Task ParseAsync_MultipleApiCalls_EmitsOneReferenceEach()
    {
        const string text = """
                            local x = Find_First_Object("UNIT_A")
                            local y = Find_Player("Rebel")
                            """;

        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);

        Assert.Equal(2, result.References.Length);
        Assert.Contains(result.References, r => r.TargetId == "UNIT_A");
        Assert.Contains(result.References, r => r.TargetId == "Rebel");
    }

    [Fact]
    public async Task ParseAsync_CaseInsensitive_FunctionName_EmitsReference()
    {
        // The game's Lua functions are case-sensitive at runtime, but our registry lookup
        // should be case-insensitive to handle any inconsistent casing in scripts.
        var result = await Build().ParseAsync(
            "file:///s.lua",
            """find_first_object("UNIT_REBEL")""",
            1, default);

        // Registry lookup is case-insensitive per design
        var reference = Assert.Single(result.References);
        Assert.Equal("UNIT_REBEL", reference.TargetId);
    }

    // ── RequireArgs ───────────────────────────────────────────────────────────

    // ── Description extraction ────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_SingleLineDocComment_DescriptionExtracted()
    {
        const string text = """
                            --- Does something useful.
                            function Foo() end
                            """;
        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);
        var sym = Assert.Single(result.Symbols);
        Assert.Equal("Does something useful.", sym.Description);
    }

    [Fact]
    public async Task ParseAsync_MultiLineDocComment_DescriptionJoinedWithNewlines()
    {
        const string text = """
                            --- Line one.
                            --- Line two.
                            function Foo() end
                            """;
        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);
        var sym = Assert.Single(result.Symbols);
        Assert.Equal("Line one.\nLine two.", sym.Description);
    }

    [Fact]
    public async Task ParseAsync_BlankLineBetweenCommentAndFunction_DescriptionIsNull()
    {
        const string text = "--- Separated.\n\nfunction Foo() end";
        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);
        var sym = Assert.Single(result.Symbols);
        Assert.Null(sym.Description);
    }

    [Fact]
    public async Task ParseAsync_AnnotationLinesExcludedFromDescription()
    {
        const string text = """
                            --- Does something.
                            ---@param x number
                            ---@return boolean
                            function Foo() end
                            """;
        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);
        var sym = Assert.Single(result.Symbols);
        Assert.Equal("Does something.", sym.Description);
    }

    [Fact]
    public async Task ParseAsync_OnlyAnnotations_DescriptionIsNull()
    {
        const string text = """
                            ---@param x number
                            function Foo() end
                            """;
        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);
        var sym = Assert.Single(result.Symbols);
        Assert.Null(sym.Description);
    }

    [Fact]
    public async Task ParseAsync_NoDocComment_DescriptionIsNull()
    {
        var result = await Build().ParseAsync("file:///s.lua", "function Foo() end", 1, default);
        var sym = Assert.Single(result.Symbols);
        Assert.Null(sym.Description);
    }

    [Fact]
    public async Task ParseAsync_RegularCommentNotDocComment_DescriptionIsNull()
    {
        // Single-dash `--` comments are NOT doc comments; only `---` lines count.
        const string text = "-- not a doc comment\nfunction Foo() end";
        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);
        var sym = Assert.Single(result.Symbols);
        Assert.Null(sym.Description);
    }

    // ── RequireArgs ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_NoRequireCalls_RequireArgs_IsEmpty()
    {
        var result = await Build().ParseAsync("file:///s.lua", "function Foo() end", 1, default);
        Assert.False(result.RequireArgs.IsDefault);
        Assert.Empty(result.RequireArgs);
    }

    [Fact]
    public async Task ParseAsync_SingleRequire_RequireArgs_ContainsRawArg()
    {
        var result = await Build().ParseAsync(
            "file:///s.lua", """require("PGStateMachine")""", 1, default);
        var arg = Assert.Single(result.RequireArgs);
        Assert.Equal("PGStateMachine", arg);
    }

    [Fact]
    public async Task ParseAsync_RelativeRequire_IsIncludedInRequireArgs()
    {
        // Relative requires are stored in RequireArgs so callers that have the callerUri
        // (hover, go-to-def, diagnostics, completion) can resolve them at query time.
        var result = await Build().ParseAsync(
            "file:///s.lua", """require("./relative/path")""", 1, default);
        Assert.Equal("./relative/path", Assert.Single(result.RequireArgs));
    }

    [Fact]
    public async Task ParseAsync_DynamicRequire_IsExcluded()
    {
        var result = await Build().ParseAsync(
            "file:///s.lua", "require(someVariable)", 1, default);
        Assert.Empty(result.RequireArgs);
    }

    [Fact]
    public async Task ParseAsync_MultipleRequires_AllRawArgsStored()
    {
        const string text = """
                            require("pgstatemachine")
                            require("eawx-std/ModContentLoader")
                            """;
        var result = await Build().ParseAsync("file:///s.lua", text, 1, default);
        Assert.Equal(2, result.RequireArgs.Length);
        Assert.Equal("pgstatemachine", result.RequireArgs[0]);
        Assert.Equal("eawx-std/ModContentLoader", result.RequireArgs[1]);
    }

    // ── annotation repository population ─────────────────────────────────────

    [Fact]
    public async Task ParseAsync_FunctionWithAnnotations_PopulatesRepository()
    {
        var repo = new LuaAnnotationRepository();
        const string uri = "file:///funcs.lua";
        const string text = """
            --- Runs the named mission.
            ---@param name string
            function RunMission(name) end
            """;

        await Build(repo).ParseAsync(uri, text, 1, default);

        Assert.True(repo.All.ContainsKey(uri));
        var annotations = repo.All[uri];
        Assert.Contains(annotations, a => !a.Params.IsDefaultOrEmpty);
    }

    [Fact]
    public async Task ParseAsync_ClassDeclarationAboveAssignment_PopulatesRepositoryWithClassDef()
    {
        var repo = new LuaAnnotationRepository();
        const string uri = "file:///types.lua";
        const string text = """
            ---@class PGUnit
            ---@field name string
            ---@field id integer
            PGUnit = {}
            """;

        await Build(repo).ParseAsync(uri, text, 1, default);

        repo.RebuildIndex();
        var cls = repo.Current.GetClass("PGUnit");
        Assert.NotNull(cls);
        Assert.Equal("PGUnit", cls!.Name);
        Assert.Equal(2, cls.Fields.Length);
    }

    [Fact]
    public async Task ParseAsync_AliasDeclarationAboveAssignment_PopulatesRepositoryWithAliasDef()
    {
        var repo = new LuaAnnotationRepository();
        const string uri = "file:///types.lua";
        const string text = """
            ---@alias GameCommandType string
            GameCommandType = nil
            """;

        await Build(repo).ParseAsync(uri, text, 1, default);

        repo.RebuildIndex();
        Assert.NotNull(repo.Current.GetAlias("GameCommandType"));
    }

    [Fact]
    public async Task ParseAsync_EnumDeclarationAboveAssignment_PopulatesRepositoryWithEnumDef()
    {
        var repo = new LuaAnnotationRepository();
        const string uri = "file:///types.lua";
        const string text = """
            ---@enum PlanetStatus
            PlanetStatus = { Owned = 0, Contested = 1 }
            """;

        await Build(repo).ParseAsync(uri, text, 1, default);

        repo.RebuildIndex();
        Assert.NotNull(repo.Current.GetEnum("PlanetStatus"));
    }

    [Fact]
    public async Task ParseAsync_MixedDeclarationFile_PopulatesAllTypeKinds()
    {
        var repo = new LuaAnnotationRepository();
        const string uri = "file:///api.d.lua";
        const string text = """
            ---@class GameEntity
            ---@field id integer
            GameEntity = {}

            ---@alias GameEntityId integer
            GameEntityId = nil

            --- Finds by name.
            ---@param name string
            function Find_Entity(name) end
            """;

        await Build(repo).ParseAsync(uri, text, 1, default);

        repo.RebuildIndex();
        Assert.NotNull(repo.Current.GetClass("GameEntity"));
        Assert.NotNull(repo.Current.GetAlias("GameEntityId"));
    }

    [Fact]
    public async Task ParseAsync_NoFunctions_RegistersEmptyAnnotationsForUri()
    {
        var repo = new LuaAnnotationRepository();
        const string uri = "file:///empty.lua";

        await Build(repo).ParseAsync(uri, "local x = 1", 1, default);

        Assert.True(repo.All.ContainsKey(uri));
        Assert.Empty(repo.All[uri]);
    }

    // ── function annotation index ─────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_GlobalFunctionWithDocComment_RegistersFunctionAnnotationByName()
    {
        var repo = new LuaAnnotationRepository();
        const string lua = """
            --- Runs the mission.
            ---@param missionName string The mission to run.
            ---@return boolean
            function RunMission(missionName) end
            """;

        await Build(repo).ParseAsync("file:///s.lua", lua, 1, default);

        var ann = repo.GetFunctionAnnotation("RunMission");
        Assert.NotNull(ann);
        Assert.Equal("Runs the mission.", ann!.Description);
        Assert.Single(ann.Params);
        Assert.Equal("missionName", ann.Params[0].Name);
        Assert.Single(ann.Returns);
        Assert.Equal("boolean", ann.Returns[0].Type.Raw);
    }

    [Fact]
    public async Task ParseAsync_GlobalFunctionWithoutDocComment_RegistersEmptyAnnotationByName()
    {
        var repo = new LuaAnnotationRepository();

        await Build(repo).ParseAsync("file:///s.lua", "function NoDoc() end", 1, default);

        var ann = repo.GetFunctionAnnotation("NoDoc");
        Assert.NotNull(ann);
        Assert.Null(ann!.Description);
        Assert.True(ann.Params.IsDefaultOrEmpty);
    }

    [Fact]
    public async Task ParseAsync_LocalFunctionWithDocComment_RegistersFunctionAnnotationByName()
    {
        var repo = new LuaAnnotationRepository();
        const string lua = """
            --- Local helper.
            ---@param x number
            local function Helper(x) end
            """;

        await Build(repo).ParseAsync("file:///s.lua", lua, 1, default);

        var ann = repo.GetFunctionAnnotation("Helper");
        Assert.NotNull(ann);
        Assert.Equal("Local helper.", ann!.Description);
        Assert.Single(ann.Params);
        Assert.Equal("x", ann.Params[0].Name);
    }

    [Fact]
    public async Task ParseAsync_MemberFunctionWithDocComment_RegistersFunctionAnnotationBySimpleName()
    {
        var repo = new LuaAnnotationRepository();
        const string lua = """
            --- Sets the value.
            ---@param value number
            function Obj.SetValue(value) end
            """;

        await Build(repo).ParseAsync("file:///s.lua", lua, 1, default);

        var ann = repo.GetFunctionAnnotation("SetValue");
        Assert.NotNull(ann);
        Assert.Equal("Sets the value.", ann!.Description);
        Assert.Single(ann.Params);
        Assert.Equal("value", ann.Params[0].Name);
    }

    [Fact]
    public async Task ParseAsync_MethodFunctionWithDocComment_RegistersFunctionAnnotationBySimpleName()
    {
        var repo = new LuaAnnotationRepository();
        const string lua = """
            --- Gets the value.
            ---@return number
            function Obj:GetValue() end
            """;

        await Build(repo).ParseAsync("file:///s.lua", lua, 1, default);

        var ann = repo.GetFunctionAnnotation("GetValue");
        Assert.NotNull(ann);
        Assert.Equal("Gets the value.", ann!.Description);
        Assert.Single(ann.Returns);
        Assert.Equal("number", ann.Returns[0].Type.Raw);
    }
}