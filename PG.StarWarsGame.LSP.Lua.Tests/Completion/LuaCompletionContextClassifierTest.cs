// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Completion;

namespace PG.StarWarsGame.LSP.Lua.Tests.Completion;

public sealed class LuaCompletionContextClassifierTest
{
    private static LuaCompletionContext? Classify(string text, int line = 0, int character = -1)
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
        var s = Assert.IsType<StringArgContext>(ctx);
        Assert.Equal("require", s.FunctionName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, s.ParamIndex);
    }

    [Fact]
    public void Classify_CursorInsideSecondArgOfFunction_ParamIndexIsOne()
    {
        // Find_Object("UNIT", "|cursor  (second arg)
        const string text = "Find_Object(\"UNIT\", \"";
        var ctx = Classify(text, character: text.Length);
        var s = Assert.IsType<StringArgContext>(ctx);
        Assert.Equal(1, s.ParamIndex);
    }

    [Fact]
    public void Classify_CursorInsideFunctionStringArg_ReturnsFunctionName()
    {
        const string text = "Find_Player(\"";
        var ctx = Classify(text, character: text.Length);
        var s = Assert.IsType<StringArgContext>(ctx);
        Assert.Equal("Find_Player", s.FunctionName);
    }

    [Fact]
    public void Classify_CursorInsideNonArgString_ReturnsNull()
    {
        // local s = "hello|  — not inside a function call
        const string text = "local s = \"hello";
        var ctx = Classify(text, character: text.Length);
        Assert.Null(ctx);
    }

    // ── member access context ─────────────────────────────────────────────────

    [Fact]
    public void Classify_CursorAfterDot_ReturnsMemberAccessContextFieldAccess()
    {
        // "playerObj." — cursor right after the dot
        const string text = "playerObj.";
        var ctx = Classify(text, character: text.Length);
        var m = Assert.IsType<MemberAccessContext>(ctx);
        Assert.False(m.IsMethodCall);
    }

    [Fact]
    public void Classify_CursorAfterDot_ReceiverNameExtracted()
    {
        const string text = "playerObj.";
        var ctx = Classify(text, character: text.Length);
        var m = Assert.IsType<MemberAccessContext>(ctx);
        Assert.Equal("playerObj", m.ReceiverName);
    }

    [Fact]
    public void Classify_CursorAfterColon_ReturnsMemberAccessContextMethodCall()
    {
        const string text = "playerObj:";
        var ctx = Classify(text, character: text.Length);
        var m = Assert.IsType<MemberAccessContext>(ctx);
        Assert.True(m.IsMethodCall);
    }

    [Fact]
    public void Classify_CursorAfterColon_ReceiverNameExtracted()
    {
        const string text = "playerObj:";
        var ctx = Classify(text, character: text.Length);
        var m = Assert.IsType<MemberAccessContext>(ctx);
        Assert.Equal("playerObj", m.ReceiverName);
    }

    [Fact]
    public void Classify_CursorAfterDot_WithLeadingWhitespace_ReceiverNameExtracted()
    {
        const string text = "    self.";
        var ctx = Classify(text, character: text.Length);
        var m = Assert.IsType<MemberAccessContext>(ctx);
        Assert.Equal("self", m.ReceiverName);
    }

    // ── identifier context ────────────────────────────────────────────────────

    [Fact]
    public void Classify_CursorAtBareIdentifier_ReturnsIdentifierContext()
    {
        // "GetP" — partial identifier
        const string text = "GetP";
        var ctx = Classify(text, character: text.Length);
        Assert.IsType<IdentifierContext>(ctx);
    }

    [Fact]
    public void Classify_CursorAtStartOfLine_IdentifierContextAtStatementStartTrue()
    {
        // line 1 starts with only whitespace before the cursor → statement start
        const string text = "function Foo()\n    ";
        var ctx = Classify(text, 1, 4);
        var id = Assert.IsType<IdentifierContext>(ctx);
        Assert.True(id.AtStatementStart);
    }

    [Fact]
    public void Classify_CursorAfterAssignment_IdentifierContextAtStatementStartFalse()
    {
        // "local x = Get" — cursor after "= "
        const string text = "local x = Get";
        var ctx = Classify(text, character: text.Length);
        var id = Assert.IsType<IdentifierContext>(ctx);
        Assert.False(id.AtStatementStart);
    }

    [Fact]
    public void Classify_EmptyLine_IdentifierContextAtStatementStartTrue()
    {
        const string text = "";
        var ctx = Classify(text, character: 0);
        var id = Assert.IsType<IdentifierContext>(ctx);
        Assert.True(id.AtStatementStart);
    }
}