// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     The game reuses a script's Lua state for its next instance (#250). Host globals are set only
///     after Base_Definitions returns; a pooled state keeps every number, string, boolean and function
///     global the library's Flush_G leaves; without Flush_G it keeps everything.
/// </summary>
public sealed class LuaPooledStateAnalyzerTest
{
    private const string ScriptUri = "file:///mod/data/scripts/gameobject/interdictor.lua";

    private static readonly LuaPooledState Pooled = new(true, true, new HashSet<string>());

    private static IReadOnlyList<Diagnostic> Analyze(string text, LuaPooledState? state = null,
        string uri = ScriptUri)
    {
        var tree = LuaSyntaxTree.ParseText(text, new LuaParseOptions(LuaSyntaxOptions.Lua51));
        return LuaPooledStateAnalyzer.Analyze(uri, tree, state ?? Pooled, TestCatalogues.English);
    }

    private static IReadOnlyList<Diagnostic> Of(string text, DiagnosticId id, LuaPooledState? state = null)
    {
        return Analyze(text, state).Where(d => d.Code!.Value.String == id.ToString()).ToList();
    }

    // ── host globals in Definitions ──────────────────────────────────────────

    // Measured on the vanilla plans: four per game read PlayerObject in Definitions, guarded.
    [Fact]
    public void AHostGlobalReadInDefinitions_IsAnError_GuardedOrNot()
    {
        var found = Of(
            "function Definitions()\n  if PlayerObject then\n    d = PlayerObject.Get_Difficulty()\n  end\nend",
            DiagnosticIds.LuaHostGlobalInDefinitions);

        Assert.Equal(2, found.Count);
        Assert.All(found, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Contains("PlayerObject", found[0].Message);
    }

    [Fact]
    public void AHostGlobalReadInAFunctionDefinitionsCalls_IsAnError_NamingTheFunction()
    {
        var found = Assert.Single(Of("function Definitions()\n  Setup()\nend\nfunction Setup()\n  x = Object\nend",
            DiagnosticIds.LuaHostGlobalInDefinitions));

        Assert.Equal(4, found.Range.Start.Line);
        Assert.Contains("Setup", found.Message);
    }

    // The library resets them: a write is no read.
    [Fact]
    public void AHostGlobalWrittenInBaseDefinitions_IsNoRead()
    {
        Assert.Empty(Of("function Base_Definitions()\n  Object = nil\n  Target = nil\nend",
            DiagnosticIds.LuaHostGlobalInDefinitions));
    }

    [Fact]
    public void AHostGlobalReadInMain_IsFine()
    {
        Assert.Empty(Of("function main()\n  Object.Attack_Move(x)\nend", DiagnosticIds.LuaHostGlobalInDefinitions));
    }

    // ── globals bleeding into the next instance ──────────────────────────────

    [Fact]
    public void AScalarGlobalSetAtRunTime_ReadBeforeItIsSet_NotResetInDefinitions_Bleeds()
    {
        var found = Assert.Single(Of("function Definitions()\n  counter = 0\nend\nfunction main()\n" +
                                     "  if fired then return end\n  fired = true\n  counter = counter + 1\nend",
            DiagnosticIds.LuaGlobalBleedsBetweenInstances));

        Assert.Equal(DiagnosticSeverity.Warning, found.Severity);
        Assert.Equal(5, found.Range.Start.Line);
        Assert.Contains("fired", found.Message);
    }

    // Measured on the vanilla scripts: most run-time globals are set before every read.
    [Fact]
    public void AGlobalAlwaysSetBeforeItIsRead_DoesNotBleed()
    {
        Assert.Empty(Of("function main()\n  dialog = \"Dialog_A\"\n  Show(dialog)\nend",
            DiagnosticIds.LuaGlobalBleedsBetweenInstances));
    }

    // x = x + 1 reads x before writing it.
    [Fact]
    public void AReadModifyWrite_Bleeds()
    {
        Assert.Single(Of("function Unit_Prox()\n  seen = seen + 1\nend",
            DiagnosticIds.LuaGlobalBleedsBetweenInstances));
    }

    // The state machine calls a handler with OnUpdate before its first pump: a global reset there is
    // reset before any event callback reads it (14 FoC, 7 EaW hero scripts reset nearby_unit_count so).
    private const string HeroScript = "function Definitions()\n  Define_State(\"State_Init\", State_Init)\nend\n" +
                                      "function State_Init(message)\n" +
                                      "  if message == OnEnter then\n    Register_Prox(Object, Unit_Prox, 50)\n" +
                                      "  elseif message == OnUpdate then\n    nearby_unit_count = 0\n  end\nend\n" +
                                      "function Unit_Prox(self, unit)\n  nearby_unit_count = nearby_unit_count + 1\nend";

    [Fact]
    public void AGlobalResetInOnUpdate_DoesNotBleed()
    {
        Assert.Empty(Of(HeroScript, DiagnosticIds.LuaGlobalBleedsBetweenInstances,
            new LuaPooledState(true, true, new HashSet<string>()) { StateMachine = true }));
    }

    // OnEnter sleeping pumps events before the first OnUpdate: the reset comes too late.
    [Fact]
    public void AGlobalResetInOnUpdate_AfterAnOnEnterThatSleeps_Bleeds()
    {
        var sleeping = HeroScript.Replace("Register_Prox(Object, Unit_Prox, 50)",
            "Register_Prox(Object, Unit_Prox, 50)\n    Sleep(1)", StringComparison.Ordinal);

        Assert.NotEmpty(Of(sleeping, DiagnosticIds.LuaGlobalBleedsBetweenInstances,
            new LuaPooledState(true, true, new HashSet<string>()) { StateMachine = true }));
    }

    // Without the library's contract the order is not known.
    [Fact]
    public void AGlobalResetInOnUpdate_WithoutTheStateMachine_Bleeds()
    {
        Assert.NotEmpty(Of(HeroScript, DiagnosticIds.LuaGlobalBleedsBetweenInstances));
    }

    // ── the event queue ──────────────────────────────────────────────────────

    // The game leaves a reused script's event queue as it was; the library's Common_Base_Definitions
    // empties it with GetEvent.Reset() when Base_Definitions runs on release.
    [Fact]
    public void AReusedStateWhoseReleaseNeverResetsTheEventQueue_IsAWarning()
    {
        var found = Assert.Single(Of("function main()\nend", DiagnosticIds.LuaPooledScriptEventsNotReset,
            new LuaPooledState(true, true, new HashSet<string>()) { ResetsEvents = false }));

        Assert.Equal(DiagnosticSeverity.Warning, found.Severity);
    }

    [Fact]
    public void AReusedStateWhoseReleaseResetsTheEventQueue_IsFine()
    {
        Assert.Empty(Of("function main()\nend", DiagnosticIds.LuaPooledScriptEventsNotReset,
            new LuaPooledState(true, true, new HashSet<string>()) { ResetsEvents = true }));
    }

    // A library's Definitions calls the script's Declare_Goal_Type: it runs at definition time.
    [Fact]
    public void AFunctionTheLibrarysDefinitionsCalls_RunsAtDefinitionTime()
    {
        var state = new LuaPooledState(true, true, new HashSet<string>())
            { DefinitionFunctions = new HashSet<string> { "Declare_Goal_Type" } };
        var found = Analyze("function Declare_Goal_Type()\n  Category = Category .. \"X\"\n  t = Target\nend", state);

        Assert.DoesNotContain(found,
            d => d.Code!.Value.String == DiagnosticIds.LuaGlobalBleedsBetweenInstances.ToString());
        Assert.Single(found, d => d.Code!.Value.String == DiagnosticIds.LuaHostGlobalInDefinitions.ToString());
    }

    // The function's name and the reason are two texts joined into one sentence: the first used to
    // end in a comma the second put a colon after.
    [Fact]
    public void AFunctionRunAtDefinitionTime_IsNamedInOneSentence()
    {
        var state = new LuaPooledState(true, true, new HashSet<string>())
            { DefinitionFunctions = new HashSet<string> { "Declare_Goal_Type" } };
        var found = Analyze("function Declare_Goal_Type()\n  t = Target\nend", state);

        var message = Assert.Single(found,
            d => d.Code!.Value.String == DiagnosticIds.LuaHostGlobalInDefinitions.ToString()).Message;
        Assert.Equal(
            "'Target' is read in 'Declare_Goal_Type', which runs at definition time: " +
            "The game sets it only after Base_Definitions returns, so it is nil here.", message);
    }

    // The library's Base_Definitions runs again at release: what it sets is reset too.
    [Fact]
    public void AGlobalTheLibraryResets_DoesNotBleed()
    {
        Assert.Empty(Of("function main()\n  ServiceRate = 1\nend", DiagnosticIds.LuaGlobalBleedsBetweenInstances,
            new LuaPooledState(true, true, new HashSet<string> { "ServiceRate" })));
    }

    // Flush_G deletes every global table: a table set at run time does not carry over.
    [Fact]
    public void ATableOrALocal_DoesNotBleed()
    {
        Assert.Empty(Of("function main()\n  units = {}\n  local seen = true\n  for i = 1, 3 do end\nend",
            DiagnosticIds.LuaGlobalBleedsBetweenInstances));
    }

    [Fact]
    public void AParameterAssigned_DoesNotBleed()
    {
        Assert.Empty(Of("function main(target)\n  target = 5\nend", DiagnosticIds.LuaGlobalBleedsBetweenInstances));
    }

    // ScriptPoolCount = 0, or story mode: every instance gets a fresh state.
    [Fact]
    public void ANotPooledScript_DoesNotBleed()
    {
        Assert.Empty(Analyze("function main()\n  fired = true\nend",
            new LuaPooledState(false, true, new HashSet<string>())));
    }

    // ── Flush_G ──────────────────────────────────────────────────────────────

    [Fact]
    public void APooledScriptWithoutFlushG_IsAWarning()
    {
        var found = Assert.Single(Of("function main()\nend", DiagnosticIds.LuaPooledScriptWithoutFlushG,
            new LuaPooledState(true, false, new HashSet<string>())));

        Assert.Equal(DiagnosticSeverity.Warning, found.Severity);
        Assert.Equal(0, found.Range.Start.Line);
    }

    // A require that does not resolve may well bring Flush_G: nothing is claimed.
    [Fact]
    public void AnUnresolvedRequire_SilencesTheFlushGRule()
    {
        Assert.Empty(Of("function main()\nend", DiagnosticIds.LuaPooledScriptWithoutFlushG,
            new LuaPooledState(true, false, new HashSet<string>(), false)));
    }

    // An evaluator is loaded once and never handed to another instance.
    [Fact]
    public void AScriptNoHostAttachesPerInstance_IsNotPooled()
    {
        Assert.Empty(Analyze("function Evaluate()\n  fired = true\nend",
            new LuaPooledState(true, false, new HashSet<string>()), "file:///mod/data/scripts/evaluators/e.lua"));
    }

    [Fact]
    public void AFileOtherScriptsRequire_IsNotPooled()
    {
        Assert.Empty(Analyze("function main()\n  if fired then end\n  fired = true\nend",
            new LuaPooledState(true, false, new HashSet<string>()) { RequiredByOthers = true }));
    }

    // A library is loaded into the scripts that require it; pooling is theirs.
    [Fact]
    public void ALibrary_IsJudgedOnlyForDefinitions()
    {
        var found = Analyze("function main()\n  fired = true\nend",
            new LuaPooledState(true, false, new HashSet<string>()), "file:///mod/data/scripts/library/mylib.lua");

        Assert.Empty(found);
    }
}