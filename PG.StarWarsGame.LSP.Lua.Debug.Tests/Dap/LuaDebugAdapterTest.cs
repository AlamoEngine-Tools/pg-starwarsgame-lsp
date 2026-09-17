// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using PG.StarWarsGame.LSP.Lua.Debug.Dap;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;
using PG.StarWarsGame.LSP.Lua.Debug.Session;
using PG.StarWarsGame.LSP.Lua.Debug.Tests.Session;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.Dap;

/// <summary>The adapter end to end: DAP client, adapter, session, fake game, all in one process.</summary>
public sealed class LuaDebugAdapterTest : IAsyncLifetime
{
    private const int Hero = 343;
    private const int Ai = 344;
    private const string ModRoot = @"D:\Mod\Data\Scripts";
    private const string HeroPath = @"D:\Mod\Data\Scripts\GameObject\Hero.lua";
    private const string HeroGamePath = @"Data\Scripts\GameObject\Hero.lua";

    private const string HeroScript = """
                                      function Tick(unit, dt)
                                          local planet = unit.Get_Planet()
                                          local speed = 3
                                          Move(planet, speed)
                                          return speed
                                      end
                                      """;

    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeGameDebugServer _game = new();
    private DapTestHarness _harness = null!;

    public LuaDebugAdapterTest()
    {
        _game.Scripts.Add(new ScriptEntry(Hero, HeroGamePath));
        _game.Scripts.Add(new ScriptEntry(Ai, @"Data\Scripts\AI\SpaceMode\Attack.lua"));
        _game.Threads[Hero] = [new ThreadEntry(0, "main")];
        _game.Variables[(Hero, "planet")] = (LuaTypeNames.Table, "table: 0x0A1B2C3D");
        _game.Variables[(Hero, "speed")] = (LuaTypeNames.Number, "3");
        _game.Variables[(Hero, "unit")] = (LuaTypeNames.Userdata, "userdata: 0x1");
        _game.Variables[(Hero, "dt")] = (LuaTypeNames.Number, "0");
        _game.Tables[(Hero, "planet")] =
            [new TableMember(LuaTypeNames.String, "name", LuaTypeNames.String, "Coruscant")];
    }

    public async ValueTask InitializeAsync()
    {
        _harness = await DapTestHarness.StartAsync(fs => fs.AddFile(HeroPath, new MockFileData(HeroScript)));
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.DisposeAsync();
        await _game.DisposeAsync();
    }

    // -- helpers ------------------------------------------------------------------------------

    private Task<AttachResponse> AttachAsync(bool unsafeTables = false)
    {
        return _harness.Client.SendRequest("attach", new
        {
            host = _game.EndPoint.Address.ToString(),
            port = _game.EndPoint.Port,
            sourceRoots = new[] { ModRoot },
            clientName = "AetLuaDebugger:test",
            unsafeTableExpansion = unsafeTables
        }).Returning<AttachResponse>(None);
    }

    private Task<SetBreakpointsResponse> SetBreakpointsAsync(string path, params SourceBreakpoint[] breakpoints)
    {
        return _harness.Client.SendRequest("setBreakpoints", new SetBreakpointsArguments
        {
            Source = new Source { Path = path, Name = "Hero.lua" },
            Breakpoints = new Container<SourceBreakpoint>(breakpoints)
        }).Returning<SetBreakpointsResponse>(None);
    }

    private async Task<StoppedEvent> SuspendHeroAtAsync(int line, int stopsSoFar = 0)
    {
        await _game.SuspendAsync(Hero, -1,
            [$"{HeroGamePath}:1:main::", $"{HeroGamePath}:{line}:Lua:global:Tick"], _game.Threads[Hero]);
        return await _harness.WaitForAsync(() => _harness.Stopped, stopsSoFar + 1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        using var deadline = new CancellationTokenSource(timeoutMilliseconds);
        while (!condition())
            await Task.Delay(10, deadline.Token);
    }

    // -- initialize and attach ----------------------------------------------------------------

    [Fact]
    public void Initialize_AnnouncesTheCapabilitiesTheGameCanHonour()
    {
        var capabilities = _harness.Client.ServerSettings;

        Assert.True(capabilities.SupportsConfigurationDoneRequest);
        Assert.True(capabilities.SupportsLoadedSourcesRequest);
        Assert.True(capabilities.SupportsEvaluateForHovers);
        Assert.False(capabilities.SupportsConditionalBreakpoints);
        Assert.False(capabilities.SupportsLogPoints);
        Assert.False(capabilities.SupportsSetVariable);
    }

    [Fact]
    public async Task Attach_ConnectsToTheGameAndSendsInitializedOnce()
    {
        await AttachAsync();

        Assert.True(_game.HelloReceived);
        Assert.Equal("AetLuaDebugger:test", _game.ClientName);
        await _harness.WaitForAsync(() => _harness.Initialized);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(_harness.Initialized);
    }

    [Fact]
    public async Task Attach_NobodyListening_FailsWithAHint()
    {
        using var silent =
            new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var port = ((System.Net.IPEndPoint)silent.Client.LocalEndPoint!).Port;

        var attach = _harness.Client.SendRequest("attach", new { host = "127.0.0.1", port })
            .Returning<AttachResponse>(None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _harness.Time.Advance(TimeSpan.FromSeconds(11));

        await Assert.ThrowsAnyAsync<Exception>(() => attach);
        Assert.Contains("luadebug", _harness.LastErrorMessage());
    }

    // -- threads ------------------------------------------------------------------------------

    [Fact]
    public async Task Threads_WhileRunning_IsTheSingleGameThread()
    {
        await AttachAsync();

        var threads = await _harness.Client.RequestThreads(new ThreadsArguments(), None);

        var thread = Assert.Single(threads.Threads!);
        Assert.Equal(LuaDebugAdapter.GameThreadId, thread.Id);
        Assert.Equal("Game", thread.Name);
    }

    [Fact]
    public async Task Threads_WhileSuspended_IsTheStoppedScriptInstance()
    {
        await AttachAsync();
        var stopped = await SuspendHeroAtAsync(4);

        var threads = await _harness.Client.RequestThreads(new ThreadsArguments(), None);

        var thread = Assert.Single(threads.Threads!);
        Assert.Equal(stopped.ThreadId, thread.Id);
        Assert.Equal("Hero.lua [343]", thread.Name);
    }

    // -- breakpoints --------------------------------------------------------------------------

    [Fact]
    public async Task SetBreakpoints_FileUnderARoot_SendsSourceWideBreakpointsInTheGamesSpelling()
    {
        await AttachAsync();

        var response = await SetBreakpointsAsync(HeroPath, new SourceBreakpoint { Line = 4 },
            new SourceBreakpoint { Line = 5 });

        Assert.All(response.Breakpoints!, b => Assert.True(b.Verified));
        await WaitUntilAsync(() => _game.Breakpoints.Count == 2);
        Assert.All(_game.Breakpoints, b =>
        {
            Assert.Equal(-1, b.ScriptId);
            Assert.Equal(-1, b.ThreadId);
            Assert.Equal(HeroGamePath, b.SourceName);
        });
        Assert.Equal([4, 5], _game.Breakpoints.Select(b => b.Line).Order());
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task SetBreakpoints_BeforeAttach_WaitAndAreAppliedThenAnnouncedOnAttach()
    {
        var response = await SetBreakpointsAsync(HeroPath, new SourceBreakpoint { Line = 4 });

        var pending = Assert.Single(response.Breakpoints!);
        Assert.False(pending.Verified);
        Assert.NotNull(pending.Id);
        Assert.Empty(_game.Breakpoints);

        await AttachAsync();

        await WaitUntilAsync(() => _game.Breakpoints.Count == 1);
        Assert.Equal(new AddBreakpointMessage(-1, -1, HeroGamePath, 4, ""), _game.Breakpoints.Single());
        var announced = await _harness.WaitForAsync(() => _harness.BreakpointEvents);
        Assert.Equal(pending.Id, announced.Breakpoint.Id);
        Assert.True(announced.Breakpoint.Verified);
    }

    [Fact]
    public async Task SetBreakpoints_SecondCallWithFewerLines_RemovesTheDroppedOnes()
    {
        await AttachAsync();
        await SetBreakpointsAsync(HeroPath, new SourceBreakpoint { Line = 4 }, new SourceBreakpoint { Line = 5 });
        await WaitUntilAsync(() => _game.Breakpoints.Count == 2);

        await SetBreakpointsAsync(HeroPath, new SourceBreakpoint { Line = 5 });

        await WaitUntilAsync(() => _game.Breakpoints.Count == 1);
        Assert.Equal(5, _game.Breakpoints.Single().Line);
    }

    [Fact]
    public async Task SetBreakpoints_Conditional_IsReportedUnverifiedAndNotSent()
    {
        await AttachAsync();

        var response = await SetBreakpointsAsync(HeroPath, new SourceBreakpoint { Line = 4, Condition = "speed > 1" });

        var breakpoint = Assert.Single(response.Breakpoints!);
        Assert.False(breakpoint.Verified);
        Assert.Contains("condition", breakpoint.Message);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_game.Breakpoints);
    }

    [Fact]
    public async Task SetBreakpoints_FileOutsideEveryRoot_IsUnverified()
    {
        await AttachAsync();

        var response = await SetBreakpointsAsync(@"D:\Elsewhere\Thing.lua", new SourceBreakpoint { Line = 1 });

        Assert.False(response.Breakpoints!.Single().Verified);
        Assert.Contains("script roots", response.Breakpoints!.Single().Message);
    }

    // -- stop, stack, scopes, variables -------------------------------------------------------

    [Fact]
    public async Task Suspended_OnABreakpointLine_StopsWithReasonBreakpoint()
    {
        await AttachAsync();
        await SetBreakpointsAsync(HeroPath, new SourceBreakpoint { Line = 4 });

        var stopped = await SuspendHeroAtAsync(4);

        Assert.Equal(StoppedEventReason.Breakpoint, stopped.Reason);
        Assert.True(stopped.AllThreadsStopped);
        Assert.Equal("Hero.lua:4", stopped.Description);
    }

    [Fact]
    public async Task Suspended_WithoutABreakpoint_StopsWithReasonPause()
    {
        await AttachAsync();

        var stopped = await SuspendHeroAtAsync(4);

        Assert.Equal(StoppedEventReason.Pause, stopped.Reason);
    }

    [Fact]
    public async Task StackTrace_TopFirstWithResolvedPaths()
    {
        await AttachAsync();
        var stopped = await SuspendHeroAtAsync(4);

        var trace = await _harness.Client.RequestStackTrace(
            new StackTraceArguments { ThreadId = stopped.ThreadId!.Value }, None);

        Assert.Equal(2, trace.TotalFrames);
        var frames = trace.StackFrames!.ToList();
        Assert.Equal("Tick", frames[0].Name);
        Assert.Equal(4, frames[0].Line);
        Assert.Equal(HeroPath, frames[0].Source!.Path);
        Assert.Equal("Hero.lua", frames[0].Source!.Name);
        Assert.Equal("<main>", frames[1].Name);
        Assert.Equal(1, frames[1].Line);
    }

    [Fact]
    public async Task StackTrace_UnresolvableSource_KeepsTheGamePathAsName()
    {
        await AttachAsync();
        await _game.SuspendAsync(Ai, -1, [@"Data\Scripts\AI\SpaceMode\Attack.lua:9:Lua:global:Think"], []);
        var stopped = await _harness.WaitForAsync(() => _harness.Stopped);

        var trace = await _harness.Client.RequestStackTrace(
            new StackTraceArguments { ThreadId = stopped.ThreadId!.Value }, None);

        var frame = Assert.Single(trace.StackFrames!);
        Assert.Null(frame.Source!.Path);
        Assert.Equal(@"Data\Scripts\AI\SpaceMode\Attack.lua", frame.Source!.Name);
    }

    [Fact]
    public async Task Scopes_SelectsTheFrameOnTheGameAndOffersLocals()
    {
        await AttachAsync();
        var stopped = await SuspendHeroAtAsync(4);
        var trace = await _harness.Client.RequestStackTrace(
            new StackTraceArguments { ThreadId = stopped.ThreadId!.Value }, None);
        var outermost = trace.StackFrames!.Last();

        var scopes = await _harness.Client.RequestScopes(new ScopesArguments { FrameId = outermost.Id }, None);

        var scope = Assert.Single(scopes.Scopes);
        Assert.Equal("Locals", scope.Name);
        Assert.NotEqual(0, scope.VariablesReference);
        await WaitUntilAsync(() => _game.SelectedLevel == 0);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task Variables_Locals_AreTheNamesInScopeReadFromTheGame()
    {
        await AttachAsync();
        var stopped = await SuspendHeroAtAsync(4);
        var trace = await _harness.Client.RequestStackTrace(
            new StackTraceArguments { ThreadId = stopped.ThreadId!.Value }, None);
        var scopes =
            await _harness.Client.RequestScopes(new ScopesArguments { FrameId = trace.StackFrames!.First().Id }, None);

        var variables = await _harness.Client.RequestVariables(
            new VariablesArguments { VariablesReference = scopes.Scopes!.Single().VariablesReference }, None);

        var byName = variables.Variables!.ToDictionary(v => v.Name);
        Assert.Equal(["dt", "planet", "speed", "unit"], byName.Keys.Order());
        Assert.Equal("3", byName["speed"].Value);
        Assert.Equal("number", byName["speed"].Type);
        Assert.Equal("table", byName["planet"].Type);
        Assert.Equal(0, byName["planet"].VariablesReference);
    }

    [Fact]
    public async Task Variables_TableWithUnsafeExpansion_ExpandsThroughTheGame()
    {
        await AttachAsync(unsafeTables: true);
        var stopped = await SuspendHeroAtAsync(4);
        var trace = await _harness.Client.RequestStackTrace(
            new StackTraceArguments { ThreadId = stopped.ThreadId!.Value }, None);
        var scopes =
            await _harness.Client.RequestScopes(new ScopesArguments { FrameId = trace.StackFrames!.First().Id }, None);
        var locals = await _harness.Client.RequestVariables(
            new VariablesArguments { VariablesReference = scopes.Scopes!.Single().VariablesReference }, None);
        var planet = locals.Variables!.Single(v => v.Name == "planet");
        Assert.NotEqual(0, planet.VariablesReference);

        var members =
            await _harness.Client.RequestVariables(
                new VariablesArguments { VariablesReference = planet.VariablesReference }, None);

        var member = Assert.Single(members.Variables!);
        Assert.Equal("name", member.Name);
        Assert.Equal("Coruscant", member.Value);
        Assert.Equal("string", member.Type);
    }

    [Fact]
    public async Task Variables_StaleReferenceAfterContinue_IsRefused()
    {
        await AttachAsync();
        var stopped = await SuspendHeroAtAsync(4);
        var trace = await _harness.Client.RequestStackTrace(
            new StackTraceArguments { ThreadId = stopped.ThreadId!.Value }, None);
        var scopes =
            await _harness.Client.RequestScopes(new ScopesArguments { FrameId = trace.StackFrames!.First().Id }, None);
        await _harness.Client.RequestContinue(new ContinueArguments { ThreadId = stopped.ThreadId!.Value }, None);

        await Assert.ThrowsAnyAsync<Exception>(() => _harness.Client.RequestVariables(
            new VariablesArguments { VariablesReference = scopes.Scopes!.Single().VariablesReference }, None));
    }

    // -- evaluate -----------------------------------------------------------------------------

    [Fact]
    public async Task Evaluate_ReplContext_RunsTheChunkAndStripsThePrompt()
    {
        await AttachAsync();
        await SuspendHeroAtAsync(4);

        var response = await _harness.Client.RequestEvaluate(
            new EvaluateArguments { Expression = "__x = 1", Context = EvaluateArgumentsContext.Repl }, None);

        Assert.Equal("ran: __x = 1", response.Result);
    }

    [Fact]
    public async Task Evaluate_WatchContext_ReadsAPlainName()
    {
        await AttachAsync();
        await SuspendHeroAtAsync(4);

        var response = await _harness.Client.RequestEvaluate(
            new EvaluateArguments { Expression = "speed", Context = EvaluateArgumentsContext.Watch }, None);

        Assert.Equal("3", response.Result);
        Assert.Equal("number", response.Type);
    }

    [Fact]
    public async Task Evaluate_WatchContextWithAnExpression_IsRefused()
    {
        await AttachAsync();
        await SuspendHeroAtAsync(4);

        await Assert.ThrowsAnyAsync<Exception>(() => _harness.Client.RequestEvaluate(
            new EvaluateArguments { Expression = "speed + 1", Context = EvaluateArgumentsContext.Watch }, None));

        Assert.Contains("plain variable name", _harness.LastErrorMessage());
    }

    [Fact]
    public async Task Evaluate_BeforeAnyStopOrSelection_IsRefused()
    {
        await AttachAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => _harness.Client.RequestEvaluate(
            new EvaluateArguments { Expression = "speed", Context = EvaluateArgumentsContext.Watch }, None));
    }

    // -- control ------------------------------------------------------------------------------

    [Fact]
    public async Task Continue_ResumesTheGame()
    {
        await AttachAsync();
        var stopped = await SuspendHeroAtAsync(4);

        var response =
            await _harness.Client.RequestContinue(new ContinueArguments { ThreadId = stopped.ThreadId!.Value }, None);

        Assert.True(response.AllThreadsContinued);
        await WaitUntilAsync(() => !_game.IsSuspended);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task Next_ThenSuspend_StopsWithReasonStep()
    {
        await AttachAsync();
        var stopped = await SuspendHeroAtAsync(4);

        await _harness.Client.RequestNext(new NextArguments { ThreadId = stopped.ThreadId!.Value }, None);
        await WaitUntilAsync(() => _game.Armed == ArmedBreakKind.Step);
        var again = await SuspendHeroAtAsync(5, stopsSoFar: 1);

        Assert.Equal(StoppedEventReason.Step, again.Reason);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task Pause_OnTheGameThread_ArmsABreakAll()
    {
        await AttachAsync();

        await _harness.Client.RequestPause(new PauseArguments { ThreadId = LuaDebugAdapter.GameThreadId }, None);

        await WaitUntilAsync(() => _game.Armed == ArmedBreakKind.BreakAll);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task Pause_WhileSuspended_IsRefusedWithoutReachingTheGame()
    {
        await AttachAsync();
        await SuspendHeroAtAsync(4);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _harness.Client.RequestPause(new PauseArguments { ThreadId = LuaDebugAdapter.GameThreadId }, None));

        Assert.Empty(_game.Violations);
    }

    // -- sources, output, termination ---------------------------------------------------------

    [Fact]
    public async Task LoadedSources_ListsEachFileOnce()
    {
        _game.Scripts.Add(new ScriptEntry(400, HeroGamePath));
        await AttachAsync();

        var sources = await _harness.Client.RequestLoadedSources(new LoadedSourcesArguments(), None);

        Assert.Equal(2, sources.Sources!.Count());
        Assert.Contains(sources.Sources!, s => s.Path == HeroPath);
        Assert.Contains(sources.Sources!, s => s.Path is null && s.Name == @"Data\Scripts\AI\SpaceMode\Attack.lua");
    }

    [Fact]
    public async Task Output_FromTheGame_IsForwardedByCategory()
    {
        await AttachAsync();

        await _game.OutputAsync(1, "type = number, value = 1.000000");
        await _game.OutputAsync(0, "LuaScript: \"Hero\", Warning: careful\n");
        await _harness.WaitForAsync(() => _harness.Output, 2);

        Assert.Equal(OutputEventCategory.StandardOutput, _harness.Output[0].Category);
        Assert.Equal("type = number, value = 1.000000\n", _harness.Output[0].Output);
        Assert.Equal(OutputEventCategory.Console, _harness.Output[1].Category);
    }

    [Fact]
    public async Task GameGoesAway_SendsTerminated()
    {
        await AttachAsync();

        await _game.GoodbyeAsync();

        await _harness.WaitForAsync(() => _harness.Terminated);
    }

    [Fact]
    public async Task Disconnect_SaysGoodbyeToTheGame()
    {
        await AttachAsync();

        await _harness.Client.RequestDisconnect(new DisconnectArguments(), None);

        await WaitUntilAsync(() => _game.GoodbyeReceived);
        Assert.True(_harness.Adapter.Exited.IsCompleted);
    }

    // -- launch -------------------------------------------------------------------------------

    [Fact]
    public async Task Launch_StartsTheProgramWithItsArgumentsAndAttaches()
    {
        await _harness.Client.SendRequest("launch", new
        {
            program = @"D:\Game\StarWarsI.exe",
            args = new[] { "MODPATH=Mods\\MyMod" },
            host = _game.EndPoint.Address.ToString(),
            port = _game.EndPoint.Port,
            sourceRoots = new[] { ModRoot }
        }).Returning<LaunchResponse>(None);

        var launch = Assert.Single(_harness.Launcher.Launches);
        Assert.Equal(@"D:\Game\StarWarsI.exe", launch.Program);
        Assert.Equal(["MODPATH=Mods\\MyMod"], launch.Arguments);
        Assert.True(_game.HelloReceived);
        await _harness.WaitForAsync(() => _harness.Initialized);
    }

    [Fact]
    public async Task Launch_ProgramExitsBeforeTheHandshake_FailsNamingTheExitCode()
    {
        _harness.Launcher.ExitImmediatelyWith = 7;
        using var silent =
            new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var port = ((System.Net.IPEndPoint)silent.Client.LocalEndPoint!).Port;

        var launch = _harness.Client.SendRequest("launch", new { program = @"D:\Game\StarWarsI.exe", port })
            .Returning<LaunchResponse>(None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _harness.Time.Advance(TimeSpan.FromSeconds(11));

        await Assert.ThrowsAnyAsync<Exception>(() => launch);
        Assert.Contains("exited with code 7", _harness.LastErrorMessage());
    }

    [Fact]
    public async Task Disconnect_AfterLaunchWithTerminate_KillsTheProcess()
    {
        await _harness.Client.SendRequest("launch", new
        {
            program = @"D:\Game\StarWarsI.exe",
            host = _game.EndPoint.Address.ToString(),
            port = _game.EndPoint.Port
        }).Returning<LaunchResponse>(None);

        await _harness.Client.RequestDisconnect(new DisconnectArguments { TerminateDebuggee = true }, None);

        Assert.True(_harness.Launcher.LastProcess!.Killed);
        await WaitUntilAsync(() => _game.GoodbyeReceived);
    }

    [Fact]
    public async Task Disconnect_AfterLaunchWithoutTerminate_LeavesTheProcessRunning()
    {
        await _harness.Client.SendRequest("launch", new
        {
            program = @"D:\Game\StarWarsI.exe",
            host = _game.EndPoint.Address.ToString(),
            port = _game.EndPoint.Port
        }).Returning<LaunchResponse>(None);

        await _harness.Client.RequestDisconnect(new DisconnectArguments { TerminateDebuggee = false }, None);

        Assert.False(_harness.Launcher.LastProcess!.Killed);
    }

    // -- the scripts view: eawLua/* requests and the scriptsChanged event ------------------------

    private Task<LuaScriptsResult> ScriptsAsync()
    {
        return _harness.Client.SendRequest(LuaCustomMessages.Scripts, new { }).Returning<LuaScriptsResult>(None);
    }

    private Task<LuaScriptsResult> RefreshAsync(int? scriptId = null)
    {
        return _harness.Client.SendRequest(LuaCustomMessages.Refresh, new LuaRefreshArguments { ScriptId = scriptId })
            .Returning<LuaScriptsResult>(None);
    }

    private Task<LuaScriptsResult> SelectScriptAsync(int scriptId)
    {
        return _harness.Client
            .SendRequest(LuaCustomMessages.SelectScript, new LuaSelectScriptArguments { ScriptId = scriptId })
            .Returning<LuaScriptsResult>(None);
    }

    private Task<LuaScriptsResult> BreakThreadAsync(int scriptId, int threadIndex)
    {
        return _harness.Client
            .SendRequest(LuaCustomMessages.BreakThread,
                new LuaBreakThreadArguments { ScriptId = scriptId, ThreadIndex = threadIndex })
            .Returning<LuaScriptsResult>(None);
    }

    [Fact]
    public async Task Scripts_BeforeAttach_IsRefused()
    {
        await Assert.ThrowsAnyAsync<Exception>(ScriptsAsync);

        Assert.Equal("Not attached to the game", _harness.LastErrorMessage());
    }

    [Fact]
    public async Task Scripts_AfterAttach_ListsEveryInstanceResolvedAgainstTheRoots()
    {
        await AttachAsync();

        var result = await ScriptsAsync();

        Assert.Equal(LuaRunStates.Running, result.RunState);
        Assert.Equal([Hero, Ai], result.Scripts.Select(s => s.ScriptId).Order());
        var hero = result.Scripts.Single(s => s.ScriptId == Hero);
        Assert.Equal("Hero.lua", hero.Name);
        Assert.Equal(HeroGamePath, hero.GamePath);
        Assert.Equal(HeroPath, hero.Path);
        Assert.False(hero.Attached);
        Assert.False(hero.IsContext);
        Assert.False(hero.IsSuspended);
        Assert.Null(hero.Threads);
        var ai = result.Scripts.Single(s => s.ScriptId == Ai);
        Assert.Equal("Attack.lua", ai.Name);
        Assert.Null(ai.Path);
    }

    [Fact]
    public async Task Scripts_GameAddsAndRemovesAnInstance_SendsScriptsChangedAndFollows()
    {
        await AttachAsync();

        await _game.AddScriptAsync(new ScriptEntry(345, @"Data\Scripts\GameObject\Hero.lua"));
        var added = await _harness.WaitForAsync(() => _harness.ScriptsChanged);
        Assert.Equal(LuaScriptsChangedReasons.ScriptAdded, added.Reason);
        Assert.Equal(345, added.ScriptId);
        Assert.Contains((await ScriptsAsync()).Scripts, s => s.ScriptId == 345);

        await _game.RemoveScriptAsync(345);
        var removed = await _harness.WaitForAsync(() => _harness.ScriptsChanged, 2);
        Assert.Equal(LuaScriptsChangedReasons.ScriptRemoved, removed.Reason);
        Assert.Equal(345, removed.ScriptId);
        Assert.DoesNotContain((await ScriptsAsync()).Scripts, s => s.ScriptId == 345);
    }

    [Fact]
    public async Task Refresh_WithAScriptId_LoadsThatScriptsThreadsFromTheGame()
    {
        await AttachAsync();

        var result = await RefreshAsync(Hero);

        var hero = result.Scripts.Single(s => s.ScriptId == Hero);
        Assert.NotNull(hero.Threads);
        var thread = Assert.Single(hero.Threads);
        Assert.Equal(0, thread.ThreadIndex);
        Assert.Equal("main", thread.Name);
        Assert.Contains(_game.Received, m => m is RequestThreadListMessage { ScriptId: Hero });
        Assert.Null(result.Scripts.Single(s => s.ScriptId == Ai).Threads);
    }

    [Fact]
    public async Task Refresh_WithoutAScriptId_ReloadsTheScriptListFromTheGame()
    {
        await AttachAsync();
        _game.Scripts.Add(new ScriptEntry(500, @"Data\Scripts\Story\Late.lua"));

        var result = await RefreshAsync();

        Assert.Contains(result.Scripts, s => s.ScriptId == 500);
        Assert.Equal(2, _game.Received.Count(m => m is RequestScriptListMessage));
    }

    [Fact]
    public async Task SelectScript_ArmsABreakInThatScript()
    {
        await AttachAsync();

        var result = await SelectScriptAsync(Hero);

        await WaitUntilAsync(() => _game.ContextScriptId == Hero && _game.Armed == ArmedBreakKind.BreakAll);
        Assert.Equal(LuaRunStates.BreakArmed, result.RunState);
        Assert.True(result.Scripts.Single(s => s.ScriptId == Hero).IsContext);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task SelectScript_TheContextScriptAgain_ArmsABreakAllInsteadOfReselecting()
    {
        await AttachAsync();
        await SuspendHeroAtAsync(4);
        await _harness.Client.RequestContinue(new ContinueArguments { ThreadId = Hero + 2 }, None);

        await SelectScriptAsync(Hero);

        await WaitUntilAsync(() => _game.Armed == ArmedBreakKind.BreakAll);
        Assert.DoesNotContain(_game.Received, m => m is SelectScriptMessage);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task SelectScript_WhileSuspended_IsRefusedWithoutReachingTheGame()
    {
        await AttachAsync();
        await SuspendHeroAtAsync(4);

        await Assert.ThrowsAnyAsync<Exception>(() => SelectScriptAsync(Ai));

        Assert.Contains("suspended", _harness.LastErrorMessage(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task BreakThread_OnAnotherScript_SelectsItLoadsItsThreadsAndArmsTheThread()
    {
        await AttachAsync();

        var result = await BreakThreadAsync(Hero, 0);

        await WaitUntilAsync(() => _game.ContextScriptId == Hero && _game.Armed == ArmedBreakKind.BreakThread);
        Assert.Contains(_game.Received, m => m is BreakThreadMessage { ThreadId: 0 });
        Assert.Equal(LuaRunStates.BreakArmed, result.RunState);
        Assert.NotNull(result.Scripts.Single(s => s.ScriptId == Hero).Threads);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task BreakThread_UnknownThread_IsRefusedWithTheReason()
    {
        await AttachAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => BreakThreadAsync(Hero, 7));

        Assert.Contains("Thread 7", _harness.LastErrorMessage());
        Assert.DoesNotContain(_game.Received, m => m is BreakThreadMessage);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task Suspended_SendsScriptsChangedNamingTheStoppedScript()
    {
        await AttachAsync();

        await SuspendHeroAtAsync(4);

        var changed = await _harness.WaitForAsync(() => _harness.ScriptsChanged);
        Assert.Equal(LuaScriptsChangedReasons.Suspended, changed.Reason);
        Assert.Equal(Hero, changed.ScriptId);
        var result = await ScriptsAsync();
        Assert.Equal(LuaRunStates.Suspended, result.RunState);
        var hero = result.Scripts.Single(s => s.ScriptId == Hero);
        Assert.True(hero.IsSuspended);
        Assert.True(hero.IsContext);
        Assert.NotNull(hero.Threads);
    }

    [Fact]
    public async Task Continue_SendsScriptsChangedForTheStateChange()
    {
        await AttachAsync();
        await SuspendHeroAtAsync(4);
        await _harness.WaitForAsync(() => _harness.ScriptsChanged);

        await _harness.Client.RequestContinue(new ContinueArguments { ThreadId = Hero + 2 }, None);

        var changed = await _harness.WaitForAsync(() => _harness.ScriptsChanged, 2);
        Assert.Equal(LuaScriptsChangedReasons.StateChanged, changed.Reason);
        Assert.Null(changed.ScriptId);
        Assert.Equal(LuaRunStates.Running, (await ScriptsAsync()).RunState);
    }
}