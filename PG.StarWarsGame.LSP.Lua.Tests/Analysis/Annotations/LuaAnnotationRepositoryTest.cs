// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis.Annotations;

public sealed class LuaAnnotationRepositoryTest
{
    private const string UriA = "file:///a.lua";
    private const string UriB = "file:///b.lua";

    private static LuaClassDefinition Class(string name) =>
        new(name, false, ImmutableArray<string>.Empty, ImmutableArray<LuaFieldDefinition>.Empty, null);

    private static LuaAliasDefinition Alias(string name) =>
        new(name, ImmutableArray<LuaTypeRef>.Empty);

    private static LuaEnumDefinition Enum(string name) =>
        new(name, false);

    [Fact]
    public void Update_AddsAnnotations_AllContainsUri()
    {
        var repo = new LuaAnnotationRepository();

        repo.Update(UriA, [EmmyLuaAnnotations.Empty with { ClassDef = Class("Foo") }]);

        Assert.True(repo.All.ContainsKey(UriA));
    }

    [Fact]
    public void Remove_AfterUpdate_AllNoLongerContainsUri()
    {
        var repo = new LuaAnnotationRepository();
        repo.Update(UriA, [EmmyLuaAnnotations.Empty]);

        repo.Remove(UriA);

        Assert.False(repo.All.ContainsKey(UriA));
    }

    [Fact]
    public void Current_IsEmpty_BeforeRebuild()
    {
        var repo = new LuaAnnotationRepository();
        repo.Update(UriA, [EmmyLuaAnnotations.Empty with { ClassDef = Class("Foo") }]);

        Assert.Empty(repo.Current.AllTypeNames);
    }

    [Fact]
    public void RebuildIndex_WithClassDef_GetClassReturnsDefinition()
    {
        var repo = new LuaAnnotationRepository();
        repo.Update(UriA, [EmmyLuaAnnotations.Empty with { ClassDef = Class("MyClass") }]);

        repo.RebuildIndex();

        var result = repo.Current.GetClass("MyClass");
        Assert.NotNull(result);
        Assert.Equal("MyClass", result!.Name);
    }

    [Fact]
    public void RebuildIndex_WithAlias_GetAliasReturnsDefinition()
    {
        var repo = new LuaAnnotationRepository();
        repo.Update(UriA, [EmmyLuaAnnotations.Empty with { AliasDef = Alias("MyAlias") }]);

        repo.RebuildIndex();

        var result = repo.Current.GetAlias("MyAlias");
        Assert.NotNull(result);
        Assert.Equal("MyAlias", result!.Name);
    }

    [Fact]
    public void RebuildIndex_WithEnum_GetEnumReturnsDefinition()
    {
        var repo = new LuaAnnotationRepository();
        repo.Update(UriA, [EmmyLuaAnnotations.Empty with { EnumDef = Enum("MyEnum") }]);

        repo.RebuildIndex();

        var result = repo.Current.GetEnum("MyEnum");
        Assert.NotNull(result);
        Assert.Equal("MyEnum", result!.Name);
    }

    [Fact]
    public void RebuildIndex_AfterRemove_ClassNoLongerAvailable()
    {
        var repo = new LuaAnnotationRepository();
        repo.Update(UriA, [EmmyLuaAnnotations.Empty with { ClassDef = Class("Gone") }]);
        repo.RebuildIndex();
        Assert.NotNull(repo.Current.GetClass("Gone")); // sanity

        repo.Remove(UriA);
        repo.RebuildIndex();

        Assert.Null(repo.Current.GetClass("Gone"));
    }

    [Fact]
    public void AllTypeNames_AfterRebuild_ContainsAllRegisteredTypes()
    {
        var repo = new LuaAnnotationRepository();
        repo.Update(UriA, [EmmyLuaAnnotations.Empty with { ClassDef = Class("Alpha") }]);
        repo.Update(UriB, [
            EmmyLuaAnnotations.Empty with { AliasDef = Alias("Beta") },
            EmmyLuaAnnotations.Empty with { EnumDef = Enum("Gamma") }
        ]);

        repo.RebuildIndex();

        Assert.Contains("Alpha", repo.Current.AllTypeNames);
        Assert.Contains("Beta", repo.Current.AllTypeNames);
        Assert.Contains("Gamma", repo.Current.AllTypeNames);
    }

    [Fact]
    public void GetClass_LookupIsCaseInsensitive()
    {
        var repo = new LuaAnnotationRepository();
        repo.Update(UriA, [EmmyLuaAnnotations.Empty with { ClassDef = Class("PGUnit") }]);
        repo.RebuildIndex();

        Assert.NotNull(repo.Current.GetClass("pgunit"));
        Assert.NotNull(repo.Current.GetClass("PGUNIT"));
    }
}
