// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis.Annotations;

public sealed class EmmyLuaAnnotationParserTest
{
    private static EmmyLuaAnnotations Parse(params string[] lines)
    {
        return EmmyLuaAnnotationParser.Parse(lines);
    }

    // ── empty / prose-only ────────────────────────────────────────────────────

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        var result = Parse();
        Assert.False(result.HasContent);
        Assert.Null(result.Description);
    }

    [Fact]
    public void Parse_ProseOnly_DescriptionPopulated()
    {
        var result = Parse("Does something.", "Second line.");
        Assert.Equal("Does something.\nSecond line.", result.Description);
        Assert.Empty(result.Params);
    }

    [Fact]
    public void Parse_MalformedLines_DoNotThrow()
    {
        var ex = Record.Exception(() => Parse("@", "@param", "@return", "@class", "@field", "|"));
        Assert.Null(ex);
    }

    // ── @param ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Param_ExtractsNameAndType()
    {
        var result = Parse("@param x number");
        var p = Assert.Single(result.Params);
        Assert.Equal("x", p.Name);
        Assert.Equal("number", p.Type.Raw);
        Assert.False(p.IsOptional);
        Assert.Null(p.Description);
    }

    [Fact]
    public void Parse_Param_OptionalSuffix()
    {
        var result = Parse("@param name? string the name");
        var p = Assert.Single(result.Params);
        Assert.Equal("name", p.Name);
        Assert.True(p.IsOptional);
        Assert.Equal("string", p.Type.Raw);
        Assert.Equal("the name", p.Description);
    }

    [Fact]
    public void Parse_MultipleParams_AllExtracted()
    {
        var result = Parse("@param a number", "@param b string desc");
        Assert.Equal(2, result.Params.Length);
        Assert.Equal("a", result.Params[0].Name);
        Assert.Equal("b", result.Params[1].Name);
    }

    // ── @return ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Return_ExtractsType()
    {
        var result = Parse("@return boolean");
        var r = Assert.Single(result.Returns);
        Assert.Equal("boolean", r.Type.Raw);
        Assert.Null(r.Name);
        Assert.Null(r.Description);
    }

    [Fact]
    public void Parse_Return_WithNameAndDescription()
    {
        var result = Parse("@return number count the item count");
        var r = Assert.Single(result.Returns);
        Assert.Equal("number", r.Type.Raw);
        Assert.Equal("count", r.Name);
        Assert.Equal("the item count", r.Description);
    }

    [Fact]
    public void Parse_MultipleReturns_AllExtracted()
    {
        var result = Parse("@return boolean", "@return string|nil");
        Assert.Equal(2, result.Returns.Length);
    }

    // ── @class ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Class_ExtractsName()
    {
        var result = Parse("@class Vec3");
        Assert.NotNull(result.ClassDef);
        Assert.Equal("Vec3", result.ClassDef!.Name);
        Assert.False(result.ClassDef.IsExact);
        Assert.Empty(result.ClassDef.Parents);
    }

    [Fact]
    public void Parse_Class_WithParent()
    {
        var result = Parse("@class Child : Parent");
        Assert.Equal("Child", result.ClassDef!.Name);
        Assert.Equal("Parent", Assert.Single(result.ClassDef.Parents));
    }

    [Fact]
    public void Parse_Class_WithMultipleParents()
    {
        var result = Parse("@class Foo : Base, Mixin");
        Assert.Equal(["Base", "Mixin"], result.ClassDef!.Parents);
    }

    [Fact]
    public void Parse_Class_ExactModifier()
    {
        var result = Parse("@class (exact) Strict");
        Assert.True(result.ClassDef!.IsExact);
        Assert.Equal("Strict", result.ClassDef.Name);
    }

    // ── @field ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ClassWithFields_FieldsAttachedToClass()
    {
        var result = Parse("@class Vec3", "@field x number", "@field y number the y component");
        Assert.NotNull(result.ClassDef);
        Assert.Equal(2, result.ClassDef!.Fields.Length);
        Assert.Equal("x", result.ClassDef.Fields[0].Name);
        Assert.Equal("number", result.ClassDef.Fields[0].Type.Raw);
        Assert.Equal("y", result.ClassDef.Fields[1].Name);
        Assert.Equal("the y component", result.ClassDef.Fields[1].Description);
    }

    [Fact]
    public void Parse_Field_WithAccessModifier()
    {
        var result = Parse("@class Foo", "@field private secret string");
        var f = Assert.Single(result.ClassDef!.Fields);
        Assert.Equal(LuaAccessModifier.Private, f.Access);
        Assert.Equal("secret", f.Name);
    }

    [Fact]
    public void Parse_Field_OptionalName()
    {
        var result = Parse("@class Foo", "@field key? string");
        var f = Assert.Single(result.ClassDef!.Fields);
        Assert.Equal("key", f.Name);
        Assert.True(f.IsOptional);
    }

    // ── @type ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Type_ExtractsRaw()
    {
        var result = Parse("@type string|nil");
        Assert.NotNull(result.TypeAnnotation);
        Assert.Equal("string|nil", result.TypeAnnotation!.Raw);
    }

    // ── @alias ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Alias_SimpleInline()
    {
        var result = Parse("@alias Color string");
        Assert.NotNull(result.AliasDef);
        Assert.Equal("Color", result.AliasDef!.Name);
        // Inline single-type alias - variants parsed from the rest of the line
        Assert.Equal("string", Assert.Single(result.AliasDef.Variants).Raw);
    }

    [Fact]
    public void Parse_Alias_UnionVariantsOnSeparateLines()
    {
        var result = Parse("@alias Color", "| \"red\"", "| \"blue\"", "| \"green\"");
        Assert.Equal("Color", result.AliasDef!.Name);
        Assert.Equal(3, result.AliasDef.Variants.Length);
        Assert.Equal("\"red\"", result.AliasDef.Variants[0].Raw);
    }

    // ── @enum ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Enum_ExtractsName()
    {
        var result = Parse("@enum Direction");
        Assert.NotNull(result.EnumDef);
        Assert.Equal("Direction", result.EnumDef!.Name);
        Assert.False(result.EnumDef.UseKeys);
    }

    [Fact]
    public void Parse_Enum_KeyAttribute()
    {
        var result = Parse("@enum (key) Direction");
        Assert.True(result.EnumDef!.UseKeys);
        Assert.Equal("Direction", result.EnumDef.Name);
    }

    // ── @overload ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Overload_RawStringStored()
    {
        var result = Parse("@overload fun(name: string): void");
        Assert.Equal("fun(name: string): void", Assert.Single(result.Overloads));
    }

    [Fact]
    public void Parse_MultipleOverloads()
    {
        var result = Parse("@overload fun(x: number): number", "@overload fun(x: string): string");
        Assert.Equal(2, result.Overloads.Length);
    }

    // ── @generic ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Generic_ExtractsName()
    {
        var result = Parse("@generic T");
        Assert.Equal("T", Assert.Single(result.GenericParams));
    }

    [Fact]
    public void Parse_Generic_WithConstraint_NameOnly()
    {
        var result = Parse("@generic T : number");
        Assert.Equal("T", Assert.Single(result.GenericParams));
    }

    [Fact]
    public void Parse_Generic_MultipleInOneLine()
    {
        var result = Parse("@generic T, U");
        Assert.Equal(["T", "U"], result.GenericParams);
    }

    // ── Tier 2: documentation markers ────────────────────────────────────────

    [Fact]
    public void Parse_Deprecated_SetsFlag()
    {
        Assert.True(Parse("@deprecated").IsDeprecated);
    }

    [Fact]
    public void Parse_Nodiscard_SetsFlag()
    {
        Assert.True(Parse("@nodiscard").IsNodiscard);
    }

    [Fact]
    public void Parse_Async_SetsFlag()
    {
        Assert.True(Parse("@async").IsAsync);
    }

    [Fact]
    public void Parse_See_StoresReference()
    {
        var result = Parse("@see SomeSymbol");
        Assert.Equal("SomeSymbol", Assert.Single(result.SeeRefs));
    }

    [Fact]
    public void Parse_Private_SetsAccessModifier()
    {
        Assert.Equal(LuaAccessModifier.Private, Parse("@private").AccessModifier);
    }

    [Fact]
    public void Parse_Protected_SetsAccessModifier()
    {
        Assert.Equal(LuaAccessModifier.Protected, Parse("@protected").AccessModifier);
    }

    [Fact]
    public void Parse_Package_SetsAccessModifier()
    {
        Assert.Equal(LuaAccessModifier.Package, Parse("@package").AccessModifier);
    }

    // ── Tier 3: silently ignored ──────────────────────────────────────────────

    [Fact]
    public void Parse_Tier3Tags_SilentlyIgnored()
    {
        var result = Parse(
            "@cast x number",
            "@diagnostic disable",
            "@meta",
            "@module 'foo'",
            "@operator add(number):number",
            "@source ./foo.lua",
            "@version 5.1",
            "@vararg number",
            "@xmlref XmlObject",
            "@Override");

        Assert.False(result.HasContent);
    }

    // ── combined block ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_FullFunctionBlock_AllFieldsPopulated()
    {
        var result = Parse(
            "Moves the unit to the given position.",
            "@deprecated",
            "@param unit userdata the unit",
            "@param pos userdata the target position",
            "@return boolean success");

        Assert.Equal("Moves the unit to the given position.", result.Description);
        Assert.True(result.IsDeprecated);
        Assert.Equal(2, result.Params.Length);
        Assert.Equal("unit", result.Params[0].Name);
        Assert.Equal("userdata", result.Params[0].Type.Raw);
        Assert.Equal("the unit", result.Params[0].Description);
        Assert.Equal("boolean", result.Returns[0].Type.Raw);
    }

    [Fact]
    public void Parse_ClassWithProseAndFields_AllPopulated()
    {
        var result = Parse(
            "@class GameObjectWrapper",
            "@field Name string the object name",
            "@field Health number current health");

        Assert.Equal("GameObjectWrapper", result.ClassDef!.Name);
        Assert.Equal(2, result.ClassDef.Fields.Length);
        Assert.Equal("Name", result.ClassDef.Fields[0].Name);
        Assert.Equal("Health", result.ClassDef.Fields[1].Name);
        Assert.Null(result.Description);
    }
}