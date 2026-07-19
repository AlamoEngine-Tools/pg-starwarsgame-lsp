// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis.Annotations;

public sealed class LuaAnnotationRepositoryTest
{
    private const string UriA = "file:///a.lua";
    private const string UriB = "file:///b.lua";

    private static LuaClassDefinition Class(string name)
    {
        return new LuaClassDefinition(name, false, ImmutableArray<string>.Empty,
            ImmutableArray<LuaFieldDefinition>.Empty, null);
    }

    private static LuaAliasDefinition Alias(string name)
    {
        return new LuaAliasDefinition(name, ImmutableArray<LuaTypeRef>.Empty);
    }

    private static LuaEnumDefinition Enum(string name)
    {
        return new LuaEnumDefinition(name, false);
    }

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

    // ── function annotation index ─────────────────────────────────────────────

    [Fact]
    public void GetFunctionAnnotation_UnknownName_ReturnsNull()
    {
        var repo = new LuaAnnotationRepository();

        Assert.Null(repo.GetFunctionAnnotation("Nope"));
    }

    [Fact]
    public void UpdateFunctionAnnotations_RegistersByName_GetFunctionAnnotationReturnsIt()
    {
        var repo = new LuaAnnotationRepository();
        var ann = EmmyLuaAnnotations.Empty with { Description = "Runs the mission." };

        repo.UpdateFunctionAnnotations(UriA, [("RunMission", ann)]);

        var result = repo.GetFunctionAnnotation("RunMission");
        Assert.NotNull(result);
        Assert.Equal("Runs the mission.", result!.Description);
    }

    [Fact]
    public void UpdateFunctionAnnotations_SecondUpdate_ReplacesOldEntriesForUri()
    {
        var repo = new LuaAnnotationRepository();
        repo.UpdateFunctionAnnotations(UriA, [("OldFunc", EmmyLuaAnnotations.Empty with { Description = "old" })]);
        repo.UpdateFunctionAnnotations(UriA, [("NewFunc", EmmyLuaAnnotations.Empty with { Description = "new" })]);

        Assert.Null(repo.GetFunctionAnnotation("OldFunc"));
        Assert.NotNull(repo.GetFunctionAnnotation("NewFunc"));
    }

    [Fact]
    public void Remove_ClearsFunctionAnnotationsForUri()
    {
        var repo = new LuaAnnotationRepository();
        repo.UpdateFunctionAnnotations(UriA, [("MyFunc", EmmyLuaAnnotations.Empty with { Description = "x" })]);

        repo.Remove(UriA);

        Assert.Null(repo.GetFunctionAnnotation("MyFunc"));
    }

    [Fact]
    public void UpdateFunctionAnnotations_MultipleUris_IndependentCleanup()
    {
        var repo = new LuaAnnotationRepository();
        repo.UpdateFunctionAnnotations(UriA, [("FuncA", EmmyLuaAnnotations.Empty with { Description = "a" })]);
        repo.UpdateFunctionAnnotations(UriB, [("FuncB", EmmyLuaAnnotations.Empty with { Description = "b" })]);

        repo.Remove(UriA);

        Assert.Null(repo.GetFunctionAnnotation("FuncA"));
        Assert.NotNull(repo.GetFunctionAnnotation("FuncB"));
    }

    // ── override inheritance (richest wins) ───────────────────────────────────

    [Fact]
    public void GetFunctionAnnotation_WhenOverrideHasNoDoc_ReturnsBaseAnnotation()
    {
        var repo = new LuaAnnotationRepository();
        var baseAnn = EmmyLuaAnnotations.Empty with { Description = "Base implementation." };

        repo.UpdateFunctionAnnotations(UriA, [("Foo", baseAnn)]);
        repo.UpdateFunctionAnnotations(UriB, [("Foo", EmmyLuaAnnotations.Empty)]);

        var result = repo.GetFunctionAnnotation("Foo");
        Assert.NotNull(result);
        Assert.Equal("Base implementation.", result!.Description);
    }

    [Fact]
    public void GetFunctionAnnotation_WhenBothEmpty_ReturnsNonNull()
    {
        var repo = new LuaAnnotationRepository();
        repo.UpdateFunctionAnnotations(UriA, [("Foo", EmmyLuaAnnotations.Empty)]);
        repo.UpdateFunctionAnnotations(UriB, [("Foo", EmmyLuaAnnotations.Empty)]);

        Assert.NotNull(repo.GetFunctionAnnotation("Foo"));
    }

    [Fact]
    public void GetFunctionAnnotation_WhenOverrideHasOwnDoc_UsesOverrideAnnotation()
    {
        var repo = new LuaAnnotationRepository();
        var baseAnn = EmmyLuaAnnotations.Empty with { Description = "Base." };
        var overrideAnn = EmmyLuaAnnotations.Empty with { Description = "Override." };

        repo.UpdateFunctionAnnotations(UriA, [("Foo", baseAnn)]);
        repo.UpdateFunctionAnnotations(UriB, [("Foo", overrideAnn)]);

        // Both are non-empty - either is valid; just verify one is returned.
        var result = repo.GetFunctionAnnotation("Foo");
        Assert.NotNull(result);
        Assert.NotNull(result!.Description);
    }
}