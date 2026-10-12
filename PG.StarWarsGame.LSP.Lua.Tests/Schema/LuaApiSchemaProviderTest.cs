// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
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
                               ---@aetref XmlObject
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
                               ---@aetref XmlObject:Faction
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
                               ---@aetref XmlObject
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
                               ---@aetref XmlObject
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
                               ---@aetref XmlObject
                               ---@param typeB string
                               ---@aetref XmlObject:Faction
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
                               ---@aetref XmlObject
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
        // Every file lua/_files.json lists, the way the server loads them.
        var dir = Path.Combine(AppContext.BaseDirectory, "schema", "lua");
        var files = Directory.GetFiles(dir, "*.d.lua").OrderBy(f => f, StringComparer.Ordinal);
        return new LuaApiSchemaProvider(files.Select(File.ReadAllText));
    }

    [Fact]
    public void ProductionSchema_DeclaresTheEngineSurface()
    {
        var provider = BuildFromProductionFile();
        // 119 global commands + 14 utility commands, plus the standard library functions.
        Assert.Contains("Create_Thread", provider.AllFunctionNames);
        Assert.Contains("GlobalValue", provider.AllFunctionNames);
        Assert.Contains("_ScriptMessage", provider.AllFunctionNames);
        Assert.Contains("tostring", provider.DeclaredGlobalNames);
        Assert.Contains("string", provider.DeclaredGlobalNames);
        Assert.Contains("Script", provider.DeclaredGlobalNames);
        Assert.Contains("Object", provider.DeclaredGlobalNames);
        Assert.DoesNotContain("math", provider.DeclaredGlobalNames);
        Assert.DoesNotContain("io", provider.DeclaredGlobalNames);
    }

    [Fact]
    public void ProductionSchema_WrapperMethodCarriesItsTag()
    {
        var provider = BuildFromProductionFile();
        var entry = Assert.Single(provider.GetXmlRefs("Play_SFX_Event"));
        Assert.Equal(0, entry.ParamIndex);
        Assert.Equal(ReferenceKind.XmlObject, entry.Kind);
        Assert.Equal("SFXEvent", entry.ExpectedTypeName);
    }

    [Fact]
    public void ProductionSchema_TextKeyParametersAreTagged()
    {
        var provider = BuildFromProductionFile();
        var entry = Assert.Single(provider.GetXmlRefs("Game_Message"));
        Assert.Equal(ReferenceKind.LocalisationKey, entry.Kind);
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
    public void ProductionSchema_FindFirstObject_NamesAGameObjectType()
    {
        var provider = BuildFromProductionFile();
        var entry = Assert.Single(provider.GetXmlRefs("Find_First_Object"));
        Assert.Equal(0, entry.ParamIndex);
        Assert.Equal(ReferenceKind.XmlObject, entry.Kind);
        Assert.Equal("GameObjectType", entry.ExpectedTypeName);
    }

    // ── @aetref: the widened reference tag ───────────────────────────────────

    [Fact]
    public void GetXmlRefs_AetrefXmlObjectWithType_IsAnXmlObjectRefWithThatType()
    {
        const string content = """
                               ---@param eventName string
                               ---@aetref XmlObject:SFXEvent
                               function Play_SFX(eventName) end
                               """;
        var entry = Assert.Single(Build(content).GetXmlRefs("Play_SFX"));
        Assert.Equal(0, entry.ParamIndex);
        Assert.Equal(ReferenceKind.XmlObject, entry.Kind);
        Assert.Equal("SFXEvent", entry.ExpectedTypeName);
    }

    [Fact]
    public void GetXmlRefs_AetrefLocalisationKey_CarriesThatKind()
    {
        const string content = """
                               ---@param textId string
                               ---@aetref LocalisationKey
                               function Game_Message(textId) end
                               """;
        var entry = Assert.Single(Build(content).GetXmlRefs("Game_Message"));
        Assert.Equal(ReferenceKind.LocalisationKey, entry.Kind);
        Assert.Null(entry.ExpectedTypeName);
    }

    // The pre-#151 spelling was dropped in #155; nothing shipped uses it.
    [Fact]
    public void GetXmlRefs_TheOldXmlrefSpelling_IsNoReferenceTag()
    {
        const string content = """
                               ---@param factionName string
                               ---@xmlref XmlObject:Faction
                               function Find_Player(factionName) end
                               """;
        Assert.Empty(Build(content).GetXmlRefs("Find_Player"));
    }

    [Fact]
    public void GetXmlRefs_AetrefOnClassMethod_FoundByMethodName()
    {
        // Wrapper methods are declared dot-style on their class table. A call site only knows the
        // method name (the receiver's type is not inferred on our side), so the lookup is by it.
        const string content = """
                               ---@class GameObject
                               GameObject = {}

                               ---@param eventName string
                               ---@aetref XmlObject:SFXEvent
                               function GameObject.Play_SFX_Event(eventName) end
                               """;
        var provider = Build(content);
        var entry = Assert.Single(provider.GetXmlRefs("Play_SFX_Event"));
        Assert.Equal(0, entry.ParamIndex);
        Assert.Equal(ReferenceKind.XmlObject, entry.Kind);
        Assert.Equal("SFXEvent", entry.ExpectedTypeName);
    }

    [Fact]
    public void GetXmlRefs_AetrefOnClassMethod_DoesNotMakeItAGlobalFunction()
    {
        const string content = """
                               ---@param eventName string
                               ---@aetref XmlObject:SFXEvent
                               function GameObject.Play_SFX_Event(eventName) end
                               """;
        Assert.DoesNotContain("Play_SFX_Event", Build(content).AllFunctionNames);
    }

    [Fact]
    public void GetXmlRefs_AetrefUnknownKind_IsIgnored()
    {
        const string content = """
                               ---@param x string
                               ---@aetref Nonsense:Thing
                               function Foo(x) end
                               """;
        Assert.Empty(Build(content).GetXmlRefs("Foo"));
    }

    // ── DeclaredGlobalNames: values the stubs declare besides functions ──────

    [Fact]
    public void DeclaredGlobalNames_ContainsTypedGlobalsAndTables()
    {
        const string content = """
                               ---@type Script
                               Script = nil
                               ---@type string
                               LUA_PATH = nil

                               ---@class stringlib
                               string = {}

                               function tostring(v) end
                               """;
        var provider = Build(content);
        Assert.Contains("Script", provider.DeclaredGlobalNames);
        Assert.Contains("LUA_PATH", provider.DeclaredGlobalNames);
        Assert.Contains("string", provider.DeclaredGlobalNames);
        Assert.Contains("tostring", provider.DeclaredGlobalNames);
        Assert.Contains("script", provider.DeclaredGlobalNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeclaredGlobalNames_IgnoresClassMethodsAndLocals()
    {
        const string content = """
                               ---@class GameObject
                               GameObject = {}
                               function GameObject.Is_Valid() end
                               local helper = 1
                               """;
        var provider = Build(content);
        Assert.DoesNotContain("Is_Valid", provider.DeclaredGlobalNames);
        Assert.DoesNotContain("helper", provider.DeclaredGlobalNames);
    }

    // A class block between functions does not leak into the function after it.
    [Fact]
    public void AClassBlock_DoesNotInterfereWithFunctionParsing()
    {
        var provider = Build("""
                             ---@class PGUnit
                             PGUnit = {}
                             ---@param typeName string
                             ---@aetref XmlObject
                             function Find_First_Object(typeName) end
                             """);

        Assert.Contains("Find_First_Object", provider.AllFunctionNames);
        Assert.Equal(0, Assert.Single(provider.GetXmlRefs("Find_First_Object")).ParamIndex);
    }
}