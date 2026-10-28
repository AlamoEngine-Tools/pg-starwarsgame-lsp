// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     The game runs Lua 5.0; the parser here and the analyzer read 5.1 (#157). What 5.1 added to the
///     syntax is reported: the length and modulo operators, '...' as an expression, long brackets
///     with '=' levels. Counted: none in the EaW or FoC scripts, two in about 10,000 files of 13 mods
///     (both in one test file).
/// </summary>
public sealed class LuaVersionSyntaxAnalyzerTest
{
    private static IReadOnlyList<Diagnostic> Analyze(string text)
    {
        var tree = LuaSyntaxTree.ParseText(text, new LuaParseOptions(LuaSyntaxOptions.Lua51));
        return LuaVersionSyntaxAnalyzer.Analyze(tree, TestCatalogues.English);
    }

    [Fact]
    public void Lua50Syntax_HasNoFindings()
    {
        Assert.Empty(Analyze("function f(a, ...)\n  local n = table.getn(arg)\n  local s = [[ long [[ nested ]] ]]\n" +
                             "  --[[ comment ]]\n  return \"#not % an operator\"\nend"));
    }

    [Fact]
    public void TheLengthOperator_IsAnError_AtTheOperator()
    {
        var found = Assert.Single(Analyze("local n = #units"));

        Assert.Equal(DiagnosticIds.LuaSyntaxNotInLua50.ToString(), found.Code!.Value.String);
        Assert.Equal(DiagnosticSeverity.Error, found.Severity);
        Assert.Equal((0, 10, 11), (found.Range.Start.Line, found.Range.Start.Character, found.Range.End.Character));
        Assert.Contains("table.getn", found.Message);
    }

    [Fact]
    public void TheModuloOperator_IsAnError()
    {
        var found = Assert.Single(Analyze("local m = a % b"));

        Assert.Equal(12, found.Range.Start.Character);
        Assert.Contains("'%'", found.Message);
    }

    [Fact]
    public void VarargsAsAnExpression_IsAnError_NamingArg()
    {
        var found = Assert.Single(Analyze("function f(...)\n  local x = ...\nend"));

        Assert.Equal(1, found.Range.Start.Line);
        Assert.Contains("arg", found.Message);
    }

    [Fact]
    public void ALongStringWithLevels_IsAnError()
    {
        var found = Assert.Single(Analyze("local s = [==[ text ]==]"));

        Assert.Contains("[[ ]]", found.Message);
    }

    // Measured on the game's lexer: "--[" opens a long comment only when another "[" follows, so
    // "--[=[" is a line comment. On one line nothing changes; over several, the rest is code.
    [Fact]
    public void ALeveledLongCommentOnOneLine_IsALineCommentToo_NoFinding()
    {
        Assert.Empty(Analyze("--[=[ comment ]=]\nlocal x = 1"));
    }

    [Fact]
    public void ALeveledLongCommentOverSeveralLines_IsAnError_TheRestIsCode()
    {
        var found = Assert.Single(Analyze("--[=[ first\nsecond line ]=]\nlocal x = 1"));

        Assert.Equal(0, found.Range.Start.Line);
        Assert.Contains("line comment", found.Message);
    }
}