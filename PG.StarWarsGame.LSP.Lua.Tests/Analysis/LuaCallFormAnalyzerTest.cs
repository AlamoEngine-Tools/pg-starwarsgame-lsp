// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     A binding that tells its call forms apart by count and by the type it finds at a position
///     (measured branch order) does something other than the script means when no form fits:
///     <c>Find_Nearest(obj, player)</c> has no string in position 2, so it reads the player and the
///     allegiance flag from positions 2 and 3, finds no flag, and searches without the player.
/// </summary>
public sealed class LuaCallFormAnalyzerTest
{
    private const string Uri = "file:///data/scripts/story/story_test.lua";

    private static readonly LuaApiSchemaProvider Stubs = new([
        """
        ---@param origin GameObject|AITargetLocation|TaskForce
        ---@param filter? string|Player
        ---@param player? Player|boolean
        ---@param match_allegiance? boolean
        ---@return GameObject
        ---@overload fun(origin: GameObject|AITargetLocation|TaskForce): GameObject
        ---@overload fun(origin: GameObject|AITargetLocation|TaskForce, filter: string): GameObject
        ---@overload fun(origin: GameObject|AITargetLocation|TaskForce, player: Player, match_allegiance: boolean): GameObject
        ---@overload fun(origin: GameObject|AITargetLocation|TaskForce, filter: string, player: Player, match_allegiance: boolean): GameObject
        function Find_Nearest(origin, filter, player, match_allegiance) end

        ---@overload fun(task_force: TaskForce|AITargetLocation|GameObject): GameObject
        ---@overload fun(target: AITargetLocation, value: number, player: Player): GameObject
        function FindDeadlyEnemy(task_force, value, player) end

        ---@param name string
        function Find_Hint(name) end

        ---@type Player
        PlayerObject = nil
        """
    ]);

    private static IReadOnlyList<Diagnostic> Analyze(string text)
    {
        var tree = LuaSyntaxTree.ParseText(text, new LuaParseOptions(LuaSyntaxOptions.Lua51));
        return LuaCallFormAnalyzer.Analyze(Uri, tree, Stubs, TestCatalogues.English);
    }

    [Theory]
    [InlineData("Find_Nearest(Object)")]
    [InlineData("Find_Nearest(Object, \"Infantry\")")]
    [InlineData("Find_Nearest(Object, PlayerObject, false)")]
    [InlineData("Find_Nearest(Object, \"Infantry\", PlayerObject, true)")]
    [InlineData("FindDeadlyEnemy(Object)")]
    [InlineData("FindDeadlyEnemy(Target, 300, PlayerObject)")]
    public void Analyze_ACallThatFitsAForm_NoDiagnostic(string call)
    {
        Assert.Empty(Analyze(call));
    }

    [Theory]
    [InlineData("Find_Nearest(Object, PlayerObject)", "(any, Player)")]
    [InlineData("Find_Nearest(Object, \"Infantry\", PlayerObject)", "(any, string, Player)")]
    [InlineData("Find_Nearest(Object, 5)", "(any, number)")]
    [InlineData("FindDeadlyEnemy(Object, 300)", "(any, number)")]
    public void Analyze_NoFormFits_WarnsWithTheArgumentsAsSeen(string call, string seen)
    {
        var diagnostic = Assert.Single(Analyze(call));
        Assert.Equal(DiagnosticIds.LuaNoCallFormFits.ToString(), diagnostic.Code?.String);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(call[..call.IndexOf('(')], diagnostic.Message);
        Assert.Contains(seen, diagnostic.Message);
        Assert.Equal(0, diagnostic.Range.Start.Character);
        Assert.Equal(call.Length, diagnostic.Range.End.Character);
    }

    // What the server cannot see, it does not judge: a variable of unknown type fits any position,
    // a local hides the global of its name, and a trailing call or `...` can spread into any count.
    [Theory]
    [InlineData("Find_Nearest(Object, some_value)")]
    [InlineData("local PlayerObject = \"Infantry\"\nFind_Nearest(Object, PlayerObject)")]
    [InlineData("Find_Nearest(Object, Get_Arguments())")]
    [InlineData("function f(...) Find_Nearest(Object, ...) end")]
    [InlineData("Find_Hint(5, 6, 7)")]
    public void Analyze_WhatCannotBeSeen_IsNotJudged(string text)
    {
        Assert.Empty(Analyze(text));
    }
}