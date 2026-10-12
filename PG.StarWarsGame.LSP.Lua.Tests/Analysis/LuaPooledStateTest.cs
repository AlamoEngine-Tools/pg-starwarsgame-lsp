// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>What a script's state is, for reuse between instances (#250).</summary>
public sealed class LuaPooledStateTest
{
    private const string Script = "file:///mod/data/scripts/gameobject/interdictor.lua";
    private const string Base = "file:///mod/data/scripts/library/pgbase.lua";
    private const string StoryMode = "file:///mod/data/scripts/library/pgstorymode.lua";

    private const string BaseLibrary = "function Flush_G() end\nfunction Base_Definitions()\n  ServiceRate = 0.2\nend";

    private static LuaPooledState StateOf(params (string Uri, string Text, string[] Requires)[] files)
    {
        var host = new FakeWorkspaceHost();
        var documents = ImmutableDictionary<string, DocumentIndex>.Empty;
        foreach (var (uri, text, requires) in files)
        {
            host.AddOrUpdate(uri, text, 1);
            documents = documents.Add(uri, new DocumentIndex(uri, 1, [], [], [.. requires]));
        }

        var fileHelper = new FileHelper(new MockFileSystem());
        var cache = TestLuaParseCache.For(host, fileHelper);
        return LuaPooledState.For(Script, cache.GetOrParse(Script)!.Tree.GetRoot(), documents, cache, fileHelper);
    }

    [Fact]
    public void AScriptReachingTheBaseLibrary_IsPooled_WithFlushG_AndTheLibrarysResets()
    {
        var state = StateOf((Script, "function main() end", ["PGBase"]), (Base, BaseLibrary, []));

        Assert.True(state.Pooled);
        Assert.True(state.HasFlushG);
        Assert.Contains("ServiceRate", state.ResetOnRelease);
    }

    [Fact]
    public void ScriptPoolCountZero_IsNotPooled()
    {
        Assert.False(StateOf((Script, "ScriptPoolCount = 0\nfunction main() end", []), (Base, BaseLibrary, [])).Pooled);
    }

    // The story library sets ScriptPoolCount = 0 for every script that requires it.
    [Fact]
    public void AStoryScript_IsNotPooled()
    {
        Assert.False(StateOf((Script, "function main() end", ["PGStoryMode"]),
            (StoryMode, "ScriptPoolCount = 0", [])).Pooled);
    }

    [Fact]
    public void WithoutTheBaseLibrary_ThereIsNoFlushG()
    {
        Assert.False(StateOf((Script, "function main() end", [])).HasFlushG);
    }

    // The script's own Definitions runs again on release as well.
    [Fact]
    public void TheScriptsOwnDefinitions_ResetsToo()
    {
        Assert.Contains("counter", StateOf((Script, "function Definitions()\n  counter = 0\nend", [])).ResetOnRelease);
    }

    // The library's Definitions calls a function the script defines: it and what it sets are
    // definition time.
    [Fact]
    public void AScriptFunctionTheLibrarysDefinitionsCalls_IsDefinitionTime()
    {
        const string interventions = "file:///mod/data/scripts/library/pginterventions.lua";
        var state = StateOf((Script, "function Declare_Goal_Type()\n  Category = \"A\"\nend", ["PGInterventions"]),
            (interventions, "function Definitions()\n  if Declare_Goal_Type then Declare_Goal_Type() end\nend", []));

        Assert.Contains("Declare_Goal_Type", state.DefinitionFunctions);
        Assert.Contains("Category", state.ResetOnRelease);
    }

    // Common_Base_Definitions, reached from Base_Definitions, empties the event queue on release.
    [Fact]
    public void ABaseDefinitionsReachingGetEventReset_ResetsEvents()
    {
        const string definitions = "file:///mod/data/scripts/library/pgbasedefinitions.lua";
        var resets = StateOf((Script, "function main() end", ["PGBaseDefinitions"]),
            (definitions, "function Base_Definitions()\n  Common_Base_Definitions()\nend\n" +
                          "function Common_Base_Definitions()\n  GetEvent.Reset()\nend", []));
        var keeps = StateOf((Script, "function Base_Definitions()\nend", []));

        Assert.True(resets.ResetsEvents);
        Assert.False(keeps.ResetsEvents);
    }

    // A file other scripts require is a library wherever it lives: FoC keeps one in GameObject/.
    [Fact]
    public void AFileAnotherScriptRequires_IsALibrary()
    {
        const string other = "file:///mod/data/scripts/gameobject/um02_droidcrate.lua";
        var state = StateOf((Script, "function Helper() end", []), (other, "", ["Interdictor"]));

        Assert.True(state.RequiredByOthers);
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