// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Completion;

namespace PG.StarWarsGame.LSP.Lua.Tests.Completion;

public sealed class LuaCompletionContextClassifierTest
{
    private static ArgumentContext? Classify(string text, int line = 0, int character = -1)
    {
        var lines = text.Split('\n');
        var col = character < 0 ? line < lines.Length ? lines[line].TrimEnd('\r').Length : 0 : character;
        return LuaCompletionContextClassifier.Classify(text, line, col);
    }

    // ── string arg context ────────────────────────────────────────────────────

    [Fact]
    public void Classify_CursorInsideRequireArg_ReturnsStringArgContext()
    {
        // cursor is just past the opening quote: require("|)
        const string text = "require(\"";
        var ctx = Classify(text, character: text.Length);
        var s = Assert.IsType<ArgumentContext>(ctx);
        Assert.Equal("require", s.FunctionName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, s.ParamIndex);
        Assert.True(s.Quoted);
    }

    [Fact]
    public void Classify_CursorInsideSecondArgOfFunction_ParamIndexIsOne()
    {
        // Find_Object("UNIT", "|cursor  (second arg)
        const string text = "Find_Object(\"UNIT\", \"";
        var ctx = Classify(text, character: text.Length);
        var s = Assert.IsType<ArgumentContext>(ctx);
        Assert.Equal(1, s.ParamIndex);
    }

    [Fact]
    public void Classify_CursorInsideFunctionStringArg_ReturnsFunctionName()
    {
        const string text = "Find_Player(\"";
        var ctx = Classify(text, character: text.Length);
        var s = Assert.IsType<ArgumentContext>(ctx);
        Assert.Equal("Find_Player", s.FunctionName);
    }

    [Fact]
    public void Classify_CursorInsideNonArgString_ReturnsNull()
    {
        // local s = "hello|  - not inside a function call
        const string text = "local s = \"hello";
        var ctx = Classify(text, character: text.Length);
        Assert.Null(ctx);
    }

    // Identifiers and members are the analyzer's to complete (#155).
    [Theory]
    [InlineData("unit.")]
    [InlineData("unit:Get")]
    [InlineData("local x = Fin")]
    [InlineData("")]
    [InlineData("function Find_Player(")]
    [InlineData("local function Find_Player(fa")]
    public void Classify_OutsideAStringArgument_ReturnsNull(string text)
    {
        Assert.Null(Classify(text));
    }

    // ── an argument before any quote ─────────────────────────────────────────

    [Theory]
    [InlineData("Find_Player(", "Find_Player", 0)]
    [InlineData("local p = Find_Player(Em", "Find_Player", 0)]
    [InlineData("Find_Object(\"UNIT\", ", "Find_Object", 1)]
    [InlineData("Story_Event(Find_Player(x), ", "Story_Event", 1)]
    [InlineData("Story_Event(Find_Player(", "Find_Player", 0)]
    public void Classify_AnArgumentBeforeAnyQuote_IsUnquoted(string text, string function, int index)
    {
        var s = Assert.IsType<ArgumentContext>(Classify(text));
        Assert.Equal((function, index, false), (s.FunctionName, s.ParamIndex, s.Quoted));
    }
}