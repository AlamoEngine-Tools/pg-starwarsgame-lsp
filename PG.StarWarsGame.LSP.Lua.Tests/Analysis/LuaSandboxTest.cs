// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     The game runs every script instance in a Lua state of its own; a required library runs in
///     the requiring script's state. Two files see each other's globals only when some script's
///     state holds both (#249).
/// </summary>
public sealed class LuaSandboxTest
{
    private const string Library = "file:///mod/data/scripts/library/pgstatemachine.lua";
    private const string Base = "file:///mod/data/scripts/library/pgbase.lua";
    private const string Interdictor = "file:///mod/data/scripts/gameobject/interdictor.lua";
    private const string Vader = "file:///mod/data/scripts/gameobject/darthvader.lua";

    private static LuaSandbox Sandbox(params (string Uri, string[] Requires)[] scripts)
    {
        var documents = scripts.ToImmutableDictionary(
            s => s.Uri,
            s => new DocumentIndex(s.Uri, 1, [], [], [.. s.Requires]));
        return LuaSandbox.Build(documents, new FileHelper(new MockFileSystem()));
    }

    [Fact]
    public void TwoObjectScripts_DoNotShareAState()
    {
        var sandbox = Sandbox((Interdictor, ["PGStateMachine"]), (Vader, ["PGStateMachine"]), (Library, []));

        Assert.False(sandbox.ShareAState(Interdictor, Vader));
    }

    // A library is held by every state that requires it; a script only by its own.
    [Fact]
    public void AFileAnotherScriptRequires_IsShared_AScriptNobodyRequires_IsNot()
    {
        var sandbox = Sandbox((Interdictor, ["PGStateMachine"]), (Library, ["PGBase"]), (Base, []), (Vader, []));

        Assert.True(sandbox.IsShared(Library));
        Assert.True(sandbox.IsShared(Base));
        Assert.False(sandbox.IsShared(Interdictor));
        Assert.False(sandbox.IsShared(Vader));
        Assert.False(sandbox.IsShared("file:///mod/data/scripts/unknown.lua"));
    }

    [Fact]
    public void AScriptAndTheLibraryItRequires_ShareAState_EitherWay()
    {
        var sandbox = Sandbox((Interdictor, ["PGStateMachine"]), (Library, []));

        Assert.True(sandbox.ShareAState(Interdictor, Library));
        Assert.True(sandbox.ShareAState(Library, Interdictor));
    }

    [Fact]
    public void ALibraryRequiredThroughAnother_IsInTheSameState()
    {
        var sandbox = Sandbox((Interdictor, ["PGStateMachine"]), (Library, ["PGBase"]), (Base, []));

        Assert.True(sandbox.ShareAState(Interdictor, Base));
    }

    // Neither library requires the other, but one script loads both: the same state.
    [Fact]
    public void TwoLibrariesOneScriptLoads_ShareAState()
    {
        var sandbox = Sandbox((Interdictor, ["PGStateMachine", "PGBase"]), (Library, []), (Base, []));

        Assert.True(sandbox.ShareAState(Library, Base));
    }

    [Fact]
    public void AFileTheIndexDoesNotHold_IsKept()
    {
        var sandbox = Sandbox((Interdictor, []));

        Assert.True(sandbox.ShareAState(Interdictor, "file:///game/data/scripts/library/pgbase.lua"));
    }

    // ── the scope of a rename ────────────────────────────────────────────────

    private const string Helper = "file:///mod/data/scripts/library/myhelpers.lua";

    [Fact]
    public void ARenameScope_IsTheScriptsState_WhenNoSharedFileNamesTheGlobal()
    {
        var sandbox = Sandbox((Interdictor, ["PGStateMachine"]), (Vader, ["PGStateMachine"]), (Library, []));

        var inScope = sandbox.RenameScope(Interdictor, [Interdictor, Vader]);

        Assert.True(inScope(Interdictor));
        Assert.False(inScope(Vader));
    }

    // The library calls Definitions: renaming one script's Definitions there renames every script's.
    [Fact]
    public void ARenameScope_TakesInEveryStateOfASharedFileNamingTheGlobal()
    {
        var sandbox = Sandbox((Interdictor, ["PGStateMachine"]), (Vader, ["PGStateMachine"]), (Library, []),
            (Helper, []));

        var inScope = sandbox.RenameScope(Interdictor, [Interdictor, Vader, Library]);

        Assert.True(inScope(Vader));
        Assert.True(inScope(Library));
    }

    // Through a chain: Vader is reached through the library, and Vader's own helper with it.
    [Fact]
    public void ARenameScope_FollowsSharedFilesTransitively()
    {
        var sandbox = Sandbox((Interdictor, ["PGStateMachine"]), (Vader, ["PGStateMachine", "MyHelpers"]),
            (Library, []), (Helper, []));

        var inScope = sandbox.RenameScope(Interdictor, [Interdictor, Library, Helper]);

        Assert.True(inScope(Helper));
    }

    [Fact]
    public void AFile_SharesAStateWithItself()
    {
        var sandbox = Sandbox((Interdictor, []), (Vader, []));

        Assert.True(sandbox.ShareAState(Interdictor, Interdictor));
    }
}