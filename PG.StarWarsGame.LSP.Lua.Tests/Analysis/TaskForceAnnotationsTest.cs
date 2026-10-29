// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using PG.StarWarsGame.LSP.Lua.Analysis.Generated;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     A plan's task forces are globals of type TaskForce, created by the engine from the plan's
///     TaskForce table: each is typed for the analyzer above the table that declares it.
/// </summary>
public sealed class TaskForceAnnotationsTest
{
    private const string Uri = "file:///mod/data/scripts/ai/raidplan.lua";

    private static IReadOnlyList<LuaAnnotationInsertion> For(string text)
    {
        var tree = LuaSyntaxTree.ParseText(text, new LuaParseOptions(LuaSyntaxOptions.Lua51));
        return new TaskForceAnnotations().For(tree, Uri).ToList();
    }

    [Fact]
    public void EachForce_IsTypedAboveItsTable()
    {
        var insertions = For("function Definitions()\n  TaskForce = {\n  { \"RaidForce\", \"Infantry = 3\" },\n" +
                             "  { \"EscortForce\", \"Fighter = 1\" }\n  }\nend");

        Assert.Equal(
        [
            (1, "---@type TaskForce\nRaidForce = nil"),
            (1, "---@type TaskForce\nEscortForce = nil")
        ], insertions.Select(i => (i.BeforeLine, string.Join("\n", i.Lines))));
    }

    // Generated as code, a name that is no identifier would break the analyzer's parse.
    [Fact]
    public void AForceNamedByNoIdentifier_IsNotTyped()
    {
        Assert.Empty(For("TaskForce = { { \"Raid Force\" }, { \"end\" } }"));
    }

    [Fact]
    public void AForceDeclaredTwice_IsTypedOnce()
    {
        var insertions =
            For("if x then\n  TaskForce = { { \"RaidForce\" } }\nelse\n  TaskForce = { { \"RaidForce\" } }\nend");

        Assert.Equal(1, Assert.Single(insertions).BeforeLine);
    }

    [Fact]
    public void AScriptWithoutATaskForceTable_HasNothing()
    {
        Assert.Empty(For("function Definitions()\n  Category = \"Raid\"\nend"));
    }
}