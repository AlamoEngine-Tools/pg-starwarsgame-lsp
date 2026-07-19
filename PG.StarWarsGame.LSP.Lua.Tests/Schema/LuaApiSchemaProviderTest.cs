// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Schema;

public sealed class LuaApiSchemaProviderTest
{
    private static LuaApiSchemaProvider Build(params string[] contents)
    {
        return new LuaApiSchemaProvider(contents);
    }

    // ── AllFunctionNames ─────────────────────────────────────────────────────

    [Fact]
    public void AllFunctionNames_Empty_WhenNoContent()
    {
        Assert.Empty(Build().AllFunctionNames);
    }

    [Fact]
    public void AllFunctionNames_ContainsRegisteredFunction()
    {
        var provider = Build("function Find_First_Object(typeName) end");
        Assert.Contains("Find_First_Object", provider.AllFunctionNames);
    }

    [Fact]
    public void AllFunctionNames_MultipleFiles_ContainsAll()
    {
        var provider = Build(
            "function Foo() end",
            "function Bar() end");
        Assert.Contains("Foo", provider.AllFunctionNames);
        Assert.Contains("Bar", provider.AllFunctionNames);
    }

    [Fact]
    public void AllFunctionNames_CaseInsensitiveLookup()
    {
        var provider = Build("function Find_First_Object(x) end");
        Assert.Contains("find_first_object", provider.AllFunctionNames, StringComparer.OrdinalIgnoreCase);
    }

    // ── GetXmlRefs ───────────────────────────────────────────────────────────

    [Fact]
    public void GetXmlRefs_NoAnnotation_ReturnsEmpty()
    {
        var provider = Build("function Foo(x) end");
        Assert.Empty(provider.GetXmlRefs("Foo"));
    }

    [Fact]
    public void GetXmlRefs_UnknownFunction_ReturnsEmpty()
    {
        var provider = Build("function Foo() end");
        Assert.Empty(provider.GetXmlRefs("NotDeclared"));
    }

    [Fact]
    public void GetXmlRefs_XmlRefOnFirstParam_ReturnsIndexZero()
    {
        const string content = """
                               ---@param typeName string
                               ---@xmlref XmlObject
                               function Find_First_Object(typeName) end
                               """;
        var entry = Assert.Single(Build(content).GetXmlRefs("Find_First_Object"));
        Assert.Equal(0, entry.ParamIndex);
        Assert.Null(entry.ExpectedTypeName);
    }

    [Fact]
    public void GetXmlRefs_TypedXmlRef_ReturnsTypeConstraint()
    {
        const string content = """
                               ---@param factionName string
                               ---@xmlref XmlObject:Faction
                               function Find_Player(factionName) end
                               """;
        var entry = Assert.Single(Build(content).GetXmlRefs("Find_Player"));
        Assert.Equal(0, entry.ParamIndex);
        Assert.Equal("Faction", entry.ExpectedTypeName);
    }

    [Fact]
    public void GetXmlRefs_XmlRefOnSecondParam_ReturnsIndexOne()
    {
        const string content = """
                               ---@param first string
                               ---@param second string
                               ---@xmlref XmlObject
                               function Foo(first, second) end
                               """;
        var entry = Assert.Single(Build(content).GetXmlRefs("Foo"));
        Assert.Equal(1, entry.ParamIndex);
        Assert.Null(entry.ExpectedTypeName);
    }

    [Fact]
    public void GetXmlRefs_XmlRefOnFirstOfThree_ReturnsIndexZero()
    {
        const string content = """
                               ---@param typeName string
                               ---@xmlref XmlObject
                               ---@param pos userdata
                               ---@param player userdata
                               function Spawn_Unit(typeName, pos, player) end
                               """;
        var entry = Assert.Single(Build(content).GetXmlRefs("Spawn_Unit"));
        Assert.Equal(0, entry.ParamIndex);
    }

    [Fact]
    public void GetXmlRefs_MultipleXmlRefs_ReturnsBoth()
    {
        const string content = """
                               ---@param typeA string
                               ---@xmlref XmlObject
                               ---@param typeB string
                               ---@xmlref XmlObject:Faction
                               function Multi(typeA, typeB) end
                               """;
        var entries = Build(content).GetXmlRefs("Multi");
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.ParamIndex == 0 && e.ExpectedTypeName == null);
        Assert.Contains(entries, e => e.ParamIndex == 1 && e.ExpectedTypeName == "Faction");
    }

    [Fact]
    public void GetXmlRefs_CaseInsensitiveLookup()
    {
        const string content = """
                               ---@param typeName string
                               ---@xmlref XmlObject
                               function Find_First_Object(typeName) end
                               """;
        var provider = Build(content);
        Assert.Single(provider.GetXmlRefs("find_first_object"));
        Assert.Single(provider.GetXmlRefs("FIND_FIRST_OBJECT"));
    }

    // ── GetFunctionDescription ───────────────────────────────────────────────

    [Fact]
    public void GetFunctionDescription_UnknownFunction_ReturnsNull()
    {
        Assert.Null(Build().GetFunctionDescription("Unknown"));
    }

    [Fact]
    public void GetFunctionDescription_FunctionWithDescription_ReturnsIt()
    {
        const string content = """
                               --- Finds the first game object of the given type.
                               function Find_First_Object(typeName) end
                               """;
        var desc = Build(content).GetFunctionDescription("Find_First_Object");
        Assert.NotNull(desc);
        Assert.Contains("Finds the first", desc);
    }

    [Fact]
    public void GetFunctionDescription_FunctionWithNoDescription_ReturnsNull()
    {
        var provider = Build("function NoDesc() end");
        Assert.Null(provider.GetFunctionDescription("NoDesc"));
    }

    [Fact]
    public void GetFunctionDescription_CaseInsensitiveLookup()
    {
        const string content = """
                               --- Some description.
                               function Foo() end
                               """;
        var provider = Build(content);
        Assert.NotNull(provider.GetFunctionDescription("foo"));
        Assert.NotNull(provider.GetFunctionDescription("FOO"));
    }

    // ── production schema ────────────────────────────────────────────────────

    private static LuaApiSchemaProvider BuildFromProductionFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "schema", "lua", "api.d.lua");
        return new LuaApiSchemaProvider([File.ReadAllText(path)]);
    }

    [Fact]
    public void ProductionSchema_ContainsKnownGlobals()
    {
        var provider = BuildFromProductionFile();
        Assert.Contains("Find_First_Object", provider.AllFunctionNames);
        Assert.Contains("Find_Player", provider.AllFunctionNames);
        Assert.Contains("Story_Event", provider.AllFunctionNames);
        Assert.Contains("Create_Position", provider.AllFunctionNames);
    }

    [Fact]
    public void ProductionSchema_FindPlayer_HasFactionConstraint()
    {
        var provider = BuildFromProductionFile();
        var entry = Assert.Single(provider.GetXmlRefs("Find_Player"));
        Assert.Equal(0, entry.ParamIndex);
        Assert.Equal("Faction", entry.ExpectedTypeName);
    }

    [Fact]
    public void ProductionSchema_FindFirstObject_HasXmlRef()
    {
        var provider = BuildFromProductionFile();
        var entry = Assert.Single(provider.GetXmlRefs("Find_First_Object"));
        Assert.Equal(0, entry.ParamIndex);
        Assert.Null(entry.ExpectedTypeName);
    }

    // ── GetClassDefinition ────────────────────────────────────────────────────

    [Fact]
    public void GetClassDefinition_ReturnsNull_WhenNotRegistered()
    {
        var provider = Build("function Foo() end");
        Assert.Null(provider.GetClassDefinition("NoSuchType"));
    }

    [Fact]
    public void GetClassDefinition_ReturnsClass_WhenDeclaredInContent()
    {
        var provider = Build("""
                             ---@class PGUnit
                             ---@field name string
                             ---@field id integer
                             PGUnit = {}
                             """);

        var cls = provider.GetClassDefinition("PGUnit");
        Assert.NotNull(cls);
        Assert.Equal("PGUnit", cls!.Name);
        Assert.Equal(2, cls.Fields.Length);
        Assert.Equal("name", cls.Fields[0].Name);
        Assert.Equal("id", cls.Fields[1].Name);
    }

    [Fact]
    public void GetClassDefinition_LookupIsCaseInsensitive()
    {
        var provider = Build("""
                             ---@class GameEntity
                             GameEntity = {}
                             """);

        Assert.NotNull(provider.GetClassDefinition("gameentity"));
        Assert.NotNull(provider.GetClassDefinition("GAMEENTITY"));
    }

    [Fact]
    public void GetClassDefinition_DoesNotInterfereWithFunctionParsing()
    {
        var provider = Build("""
                             ---@class PGUnit
                             PGUnit = {}
                             ---@param typeName string
                             ---@xmlref XmlObject
                             function Find_First_Object(typeName) end
                             """);

        Assert.NotNull(provider.GetClassDefinition("PGUnit"));
        Assert.Contains("Find_First_Object", provider.AllFunctionNames);
    }

    [Fact]
    public void LuaApiSchemaProxy_GetClassDefinition_ReturnsNull_BeforeConfigure()
    {
        var proxy = new LuaApiSchemaProxy();
        Assert.Null(proxy.GetClassDefinition("Anything"));
    }

    [Fact]
    public void LuaApiSchemaProxy_GetClassDefinition_DelegatesToInnerAfterConfigure()
    {
        var proxy = new LuaApiSchemaProxy();
        proxy.Configure(Build("""
                              ---@class PGUnit
                              PGUnit = {}
                              """));

        Assert.NotNull(proxy.GetClassDefinition("PGUnit"));
    }
}