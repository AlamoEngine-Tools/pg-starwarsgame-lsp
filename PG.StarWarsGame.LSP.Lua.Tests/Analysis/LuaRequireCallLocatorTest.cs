// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaRequireCallLocatorTest
{
    private static readonly LuaParseOptions ParseOptions = new(LuaSyntaxOptions.Lua51);

    [Fact]
    public void TryFindAt_CursorInsideStringArg_ReturnsArgTextAndRange()
    {
        const string source = "local x = require(\"PGStateMachine\")";
        var root = LuaSyntaxTree.ParseText(source, ParseOptions).GetRoot();

        // Cursor inside "PGStateMachine" - after the opening quote at column 19.
        var result = LuaRequireCallLocator.TryFindAt(root, 0, 22);

        Assert.NotNull(result);
        Assert.Equal("PGStateMachine", result!.Value.ArgText);
    }

    [Fact]
    public void TryFindAt_CursorOutsideAnyRequireCall_ReturnsNull()
    {
        const string source = "local x = require(\"PGStateMachine\")";
        var root = LuaSyntaxTree.ParseText(source, ParseOptions).GetRoot();

        var result = LuaRequireCallLocator.TryFindAt(root, 0, 2);

        Assert.Null(result);
    }

    [Fact]
    public void TryFindAt_NoRequireCallInSource_ReturnsNull()
    {
        const string source = "local x = 1";
        var root = LuaSyntaxTree.ParseText(source, ParseOptions).GetRoot();

        var result = LuaRequireCallLocator.TryFindAt(root, 0, 5);

        Assert.Null(result);
    }
}