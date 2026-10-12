// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     The state machine rules describe the game's library. A mod can ship its own, which wins over
///     the game's; the rules apply only where the script's state loads a library with the contract.
/// </summary>
public sealed class LuaStateMachineContractTest
{
    private const string Script = "file:///mod/data/scripts/gameobject/interdictor.lua";
    private const string StateMachine = "file:///mod/data/scripts/library/pgstatemachine.lua";
    private const string StoryMode = "file:///mod/data/scripts/library/pgstorymode.lua";
    private const string Helpers = "file:///mod/data/scripts/library/myhelpers.lua";

    // The library's shape, authored: what the contract check looks for, nothing of the game's text.
    private const string MachineLibrary = """
                                          function Define_State(state, f) StateMachine[state] = f end
                                          function Set_Next_State(state)
                                              if state == nil or StateMachine[state] ~= nil then NextState = state end
                                          end
                                          function Process_States()
                                              _curproc(OnEnter) _curproc(OnUpdate) _curproc(OnExit)
                                          end
                                          """;

    private const string StoryLibrary = """
                                        require("PGStateMachine")
                                        function PG_Story_Mode_Init()
                                            for key, value in pairs(StoryModeEvents) do Define_State(key, value) end
                                        end
                                        """;

    private static LuaStateMachineContract ContractOf(
        params (string Uri, string Text, string[] Requires, string[] Globals)[] files)
    {
        var host = new FakeWorkspaceHost();
        var documents = ImmutableDictionary<string, DocumentIndex>.Empty;
        foreach (var (uri, text, requires, globals) in files)
        {
            host.AddOrUpdate(uri, text, 1);
            var symbols = globals.Select(g =>
                    new GameSymbol(g, GameSymbolKind.LuaGlobal, null, new FileOrigin(uri, 0, 0), null))
                .ToImmutableArray();
            documents = documents.Add(uri, new DocumentIndex(uri, 1, symbols, [], [.. requires]));
        }

        var fileHelper = new FileHelper(new MockFileSystem());
        return LuaStateMachineContract.For(Script, documents, TestLuaParseCache.For(host, fileHelper), fileHelper);
    }

    [Fact]
    public void TheGamesLibrary_InTheScriptsState_HoldsTheContract()
    {
        var contract = ContractOf((Script, "", ["PGStateMachine"], []), (StateMachine, MachineLibrary, [], []));

        Assert.True(contract.StateMachine);
        Assert.False(contract.StoryMode);
    }

    [Fact]
    public void StoryMode_ThroughTheStoryLibrary()
    {
        var contract = ContractOf((Script, "", ["PGStoryMode"], []), (StoryMode, StoryLibrary, ["PGStateMachine"], []),
            (StateMachine, MachineLibrary, [], []));

        Assert.True(contract.StateMachine);
        Assert.True(contract.StoryMode);
    }

    // A mod's own state machine that ignores nothing: the game's contract does not hold.
    [Fact]
    public void AModsOwnLibraryWithoutTheContract_DoesNotHold()
    {
        var contract = ContractOf((Script, "", ["PGStateMachine"], []),
            (StateMachine, "function Define_State(s, f) end\nfunction Set_Next_State(s) NextState = s end", [], []));

        Assert.False(contract.StateMachine);
    }

    [Fact]
    public void AScriptNotLoadingTheLibrary_HasNoContract()
    {
        Assert.False(ContractOf((Script, "", [], []), (StateMachine, MachineLibrary, [], [])).StateMachine);
    }

    [Fact]
    public void TheOtherFilesOfTheState_LendTheirGlobals()
    {
        var contract = ContractOf((Script, "", ["PGStateMachine", "MyHelpers"], ["Own"]),
            (StateMachine, MachineLibrary, [], []), (Helpers, "", [], ["Shared_Init"]));

        Assert.Contains("Shared_Init", contract.FunctionsElsewhere);
        Assert.DoesNotContain("Own", contract.FunctionsElsewhere);
    }

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }
}