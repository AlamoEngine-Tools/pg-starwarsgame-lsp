// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     What one script instance holds: the script and everything it requires, transitively, and the
///     modules it requires that no layer ships.
/// </summary>
public sealed class LuaRequireTreeTest
{
    private const string Script = "file:///mod/data/scripts/gameobject/interdictor.lua";
    private const string Machine = "file:///mod/data/scripts/library/pgstatemachine.lua";
    private const string Base = "file:///mod/data/scripts/library/pgbase.lua";
    private const string Other = "file:///mod/data/scripts/gameobject/darthvader.lua";

    private static ImmutableDictionary<string, DocumentIndex> Documents(
        params (string Uri, string[] Requires)[] scripts)
    {
        return scripts.ToImmutableDictionary(s => s.Uri, s => new DocumentIndex(s.Uri, 1, [], [], [.. s.Requires]));
    }

    private static readonly FileHelper s_files = new(new MockFileSystem());

    [Fact]
    public void TheScriptFirst_ThenWhatItRequires_Transitively()
    {
        var documents = Documents((Script, ["PGStateMachine"]), (Machine, ["PGBase"]), (Base, []), (Other, []));

        var tree = LuaRequireTree.Of(Script, documents, s_files);

        Assert.Equal([Script, Machine, Base], tree.Files);
        Assert.Empty(tree.Unresolved);
    }

    [Fact]
    public void AModuleNoLayerShips_IsUnresolved_AsTheScriptNamesIt()
    {
        var documents = Documents((Script, ["PGStateMachine", "PGDebug"]), (Machine, ["pgevents"]));

        var tree = LuaRequireTree.Of(Script, documents, s_files);

        Assert.Equal([Script, Machine], tree.Files);
        Assert.Equal(["PGDebug", "pgevents"], tree.Unresolved);
    }

    [Fact]
    public void AModuleRequiredTwice_IsHeldOnce()
    {
        var documents = Documents((Script, ["PGStateMachine", "PGBase"]), (Machine, ["PGBase", "PGDebug"]),
            (Base, ["pgdebug"]));

        var tree = LuaRequireTree.Of(Script, documents, s_files);

        Assert.Equal([Script, Machine, Base], tree.Files);
        Assert.Equal(["PGDebug"], tree.Unresolved);
    }

    [Fact]
    public void AScriptTheIndexDoesNotHold_IsATreeOfItsOwn()
    {
        var tree = LuaRequireTree.Of(Script, Documents(), s_files);

        Assert.Equal([Script], tree.Files);
        Assert.Empty(tree.Unresolved);
    }
}