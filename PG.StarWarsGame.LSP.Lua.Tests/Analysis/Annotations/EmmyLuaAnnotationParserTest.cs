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

    // ── every other tag is the analyzer's (#155) ─────────────────────────────

    [Fact]
    public void Parse_EveryOtherTag_IsSkipped()
    {
        var result = Parse(
            "@class GameObjectWrapper",
            "@field Name string the object name",
            "@alias Command string",
            "@enum PlanetStatus",
            "@type integer",
            "@deprecated",
            "@overload fun(a: string)",
            "@generic T",
            "@see Find_Player",
            "@aetref XmlObject",
            "@meta");

        Assert.False(result.HasContent);
    }

    [Fact]
    public void Parse_FullFunctionBlock_ProseParamsAndReturnsInOrder()
    {
        var result = Parse(
            "Moves the unit to the given position.",
            "@deprecated",
            "@param unit userdata the unit",
            "@aetref XmlObject:Unit",
            "@param pos userdata the target position",
            "@return boolean success");

        Assert.Equal("Moves the unit to the given position.", result.Description);
        Assert.Equal(["unit", "pos"], result.Params.Select(p => p.Name));
        Assert.Equal("userdata", result.Params[0].Type.Raw);
        Assert.Equal("the unit", result.Params[0].Description);
        Assert.Equal("boolean", Assert.Single(result.Returns).Type.Raw);
    }
}