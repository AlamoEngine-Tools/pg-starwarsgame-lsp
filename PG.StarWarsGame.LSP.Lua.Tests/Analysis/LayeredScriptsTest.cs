// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     Require resolution, require trees and script states in every project shape (#280): a mod
///     alone, over vanilla, and over a mod over vanilla. A script is the leaf's; the libraries are
///     shipped by the lowest layer, or by every layer.
/// </summary>
public sealed class LayeredScriptsTest
{
    private const string Script = "GameObject/Interdictor.lua";
    private const string Other = "GameObject/DarthVader.lua";
    private const string Machine = "Library/PGStateMachine.lua";
    private const string Base = "Library/PGBase.lua";

    public static TheoryData<string> Setups => new(ProjectSetup.Names);

    private static Dictionary<string, DocumentIndex> Documents(params DocumentIndex[] documents)
    {
        return documents.ToDictionary(d => d.DocumentUri, DocumentUris.Comparer);
    }

    // Leaf first, root last: the highest layer's copy of a library wins, and a library only the
    // lowest layer ships is found there.
    [Theory]
    [MemberData(nameof(Setups))]
    public void ALibrary_ResolvesToTheHighestLayersCopy(string name)
    {
        var setup = ProjectSetup.Named(name);
        var documents = Documents([
            .. setup.Layers.Select(l => setup.Script(l.Rank, Base)),
            setup.Script(0, Machine),
            setup.Script(setup.LeafRank, Script, ["PGBase", "PGStateMachine"])
        ]);
        var fileHelper = new FileHelper(setup.Disk());
        var from = setup.ScriptUri(setup.LeafRank, Script);

        Assert.Equal(setup.ScriptUri(setup.LeafRank, Base),
            LuaRequireResolver.Resolve("PGBase", documents, fileHelper, from), DocumentUris.Comparer);
        Assert.Equal(setup.ScriptUri(0, Machine),
            LuaRequireResolver.Resolve("PGStateMachine", documents, fileHelper, from), DocumentUris.Comparer);
    }

    // The tree crosses layers: the script, the lowest layer's state machine, and the base library it
    // requires in the highest layer's copy.
    [Theory]
    [MemberData(nameof(Setups))]
    public void ARequireTree_HoldsEachRequiredFile_FromTheLayerThatWins(string name)
    {
        var setup = ProjectSetup.Named(name);
        var documents = Documents([
            .. setup.Layers.Select(l => setup.Script(l.Rank, Base)),
            setup.Script(0, Machine, ["PGBase"]),
            setup.Script(setup.LeafRank, Script, ["PGStateMachine"])
        ]);

        var tree = LuaRequireTree.Of(setup.ScriptUri(setup.LeafRank, Script), documents,
            new FileHelper(setup.Disk()));

        Assert.Equal(
            new[]
                {
                    setup.ScriptUri(setup.LeafRank, Script), setup.ScriptUri(0, Machine),
                    setup.ScriptUri(setup.LeafRank, Base)
                }
                .Order(StringComparer.OrdinalIgnoreCase),
            tree.Files.Order(StringComparer.OrdinalIgnoreCase), DocumentUris.Comparer);
        Assert.Empty(tree.Unresolved);
    }

    // Two scripts requiring a lower layer's library: each shares a state with the library, never with
    // the other, and the library is held by both.
    [Theory]
    [MemberData(nameof(Setups))]
    public void TwoScripts_ShareTheLibrary_NotEachOther(string name)
    {
        var setup = ProjectSetup.Named(name);
        var (script, other, machine) = (setup.ScriptUri(setup.LeafRank, Script),
            setup.ScriptUri(setup.LeafRank, Other), setup.ScriptUri(0, Machine));
        var sandbox = LuaSandbox.Build(Documents(
            setup.Script(0, Machine),
            setup.Script(setup.LeafRank, Script, ["PGStateMachine"]),
            setup.Script(setup.LeafRank, Other, ["PGStateMachine"])), new FileHelper(setup.Disk()));

        Assert.True(sandbox.ShareAState(script, machine));
        Assert.False(sandbox.ShareAState(script, other));
        Assert.True(sandbox.IsShared(machine));
        Assert.False(sandbox.IsShared(script));
        Assert.Equal(new[] { machine, other, script }.Order(StringComparer.OrdinalIgnoreCase),
            sandbox.StatesHolding(machine).Order(StringComparer.OrdinalIgnoreCase), DocumentUris.Comparer);
    }
}