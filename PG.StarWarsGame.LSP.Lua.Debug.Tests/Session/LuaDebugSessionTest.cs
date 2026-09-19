// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;
using PG.StarWarsGame.LSP.Lua.Debug.Session;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.Session;

/// <summary>
///     The session against the fake game over real loopback UDP. The session's clock is a fake so
///     timeouts, resends and the silence watchdog are driven by advancing it, never by waiting.
/// </summary>
public sealed class LuaDebugSessionTest : IAsyncLifetime
{
    private const int Hero = 343;
    private const int Ai = 344;
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeTimeProvider _time = new();
    private readonly FakeGameDebugServer _game = new();
    private readonly ILuaDebugSession _session;

    public LuaDebugSessionTest()
    {
        _game.Scripts.Add(new ScriptEntry(Hero, "Data\\Scripts\\GameObject\\Hero.lua"));
        _game.Scripts.Add(new ScriptEntry(Ai, "Data\\Scripts\\AI\\SpaceMode\\Attack.lua"));
        _game.Threads[Hero] = [new ThreadEntry(0, "main"), new ThreadEntry(2, "Hero_Thread")];
        _game.ChildScripts[Hero] = ["Data\\Scripts\\Library\\PGBase.lua"];
        _game.Variables[(Hero, "planet")] = (5, "table: 0x0A1B2C3D");
        _game.Tables[(Hero, "Small")] = [new TableMember(4, "name", 4, "Coruscant")];

        var provider = TestServices.Build(services => services.AddSingleton<TimeProvider>(_time));
        _session = provider.GetRequiredService<ILuaDebugSession>();
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        await _game.DisposeAsync();
    }

    private async Task ConnectAsync()
    {
        await _session.ConnectAsync(_game.EndPoint, "AetLuaDebugger:test", None);
    }

    private async Task SuspendHeroAsync()
    {
        await _game.SuspendAsync(Hero, 2,
            ["Data\\Scripts\\GameObject\\Hero.lua:12:main::", "Data\\Scripts\\GameObject\\Hero.lua:30:Lua:global:Tick"],
            _game.Threads[Hero]);
        await WaitUntilAsync(() => _session.RunState == DebugRunState.Suspended);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        using var deadline = new CancellationTokenSource(timeoutMilliseconds);
        while (!condition())
            await Task.Delay(10, deadline.Token);
    }

    // -- connect ------------------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_CompletesBothHandshakesAndLoadsScripts()
    {
        await ConnectAsync();

        Assert.True(_session.IsConnected);
        Assert.Equal("AetLuaDebugger:test", _game.ClientName);
        Assert.True(_game.HelloReceived);
        Assert.Equal([Hero, Ai], _session.Scripts.Keys.Order());
        Assert.Equal(DebugRunState.Running, _session.RunState);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task ConnectAsync_NobodyListening_TimesOutWithAHint()
    {
        using var silent =
            new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var endPoint = (System.Net.IPEndPoint)silent.Client.LocalEndPoint!;
        var connect = _session.ConnectAsync(endPoint, "AetLuaDebugger:test", None);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(11));

        var e = await Assert.ThrowsAsync<TimeoutException>(() => connect);
        Assert.Contains("luadebug", e.Message);
        Assert.False(_session.IsConnected);
    }

    // -- queries ------------------------------------------------------------------------------

    [Fact]
    public async Task RequestThreadsAsync_KnownScript_ReturnsAndRemembersThreads()
    {
        await ConnectAsync();

        var threads = await _session.RequestThreadsAsync(Hero, None);

        Assert.Equal(_game.Threads[Hero], threads);
        Assert.Equal(_game.Threads[Hero], _session.ThreadsByScript[Hero]);
    }

    [Fact]
    public async Task RequestThreadsAsync_UnknownScript_RefusedBeforeSending()
    {
        await ConnectAsync();

        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.RequestThreadsAsync(999, None));

        Assert.DoesNotContain(_game.Received, m => m is RequestThreadListMessage);
    }

    [Fact]
    public async Task RequestThreadsAsync_GameNeverAnswers_TimesOut()
    {
        await ConnectAsync();
        _game.IgnoreThreadRequests = true;

        var request = _session.RequestThreadsAsync(Hero, None);
        await _game.WaitForAsync<RequestThreadListMessage>();
        _time.Advance(TimeSpan.FromSeconds(6));

        await Assert.ThrowsAsync<TimeoutException>(() => request);
        Assert.True(_session.IsConnected);
    }

    [Fact]
    public async Task AttachAsync_ReturnsChildrenAndMarksAttached()
    {
        await ConnectAsync();

        var children = await _session.AttachAsync(Hero, None);

        Assert.Equal(["Data\\Scripts\\Library\\PGBase.lua"], children);
        Assert.Contains(Hero, _session.AttachedScriptIds);
        Assert.Contains(Hero, _game.AttachedScriptIds);
    }

    [Fact]
    public async Task DumpVariableAsync_ReturnsTheGamesValue()
    {
        await ConnectAsync();

        var value = await _session.DumpVariableAsync(Hero, "planet", None);

        Assert.Equal(5, value.ValueType);
        Assert.Equal("table: 0x0A1B2C3D", value.ValueText);
    }

    [Fact]
    public async Task DumpTableAsync_CorrelatesByRequestId()
    {
        await ConnectAsync();

        var first = await _session.DumpTableAsync(Hero, "Small", [], None);
        var second = await _session.DumpTableAsync(Hero, "Missing", [], None);

        Assert.Equal(_game.Tables[(Hero, "Small")], first.Members);
        Assert.Empty(second.Members);
        Assert.NotEqual(first.RequestId, second.RequestId);
    }

    [Fact]
    public async Task ExecuteTextAsync_ReturnsTheGamesReplyWithPrompt()
    {
        await ConnectAsync();

        var result = await _session.ExecuteTextAsync(Hero, "__x = 1", None);

        Assert.Equal("ran: __x = 1\r\n> ", result);
    }

    [Fact]
    public async Task ExecuteTextAsync_UnknownScript_RefusedInsteadOfHangingUntilTimeout()
    {
        await ConnectAsync();

        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.ExecuteTextAsync(999, "x", None));
    }

    // -- breakpoints --------------------------------------------------------------------------

    [Fact]
    public async Task AddBreakpointAsync_SourceWide_SendsMinusOneIdsWithoutAttaching()
    {
        await ConnectAsync();

        await _session.AddBreakpointAsync(-1, -1, "Data\\Scripts\\GameObject\\Hero.lua", 30, None);
        var sent = await _game.WaitForAsync<AddBreakpointMessage>();

        Assert.Equal(new AddBreakpointMessage(-1, -1, "Data\\Scripts\\GameObject\\Hero.lua", 30, ""), sent);
        Assert.Empty(_session.AttachedScriptIds);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task AddBreakpointAsync_ScriptScoped_AttachesFirst()
    {
        await ConnectAsync();

        await _session.AddBreakpointAsync(Hero, -1, "Data\\Scripts\\GameObject\\Hero.lua", 30, None);
        await _game.WaitForAsync<AddBreakpointMessage>();

        Assert.Contains(Hero, _session.AttachedScriptIds);
        Assert.Single(_game.Breakpoints, b => b.ScriptId == Hero);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task AddBreakpointAsync_UnknownThread_Refused()
    {
        await ConnectAsync();
        await _session.RequestThreadsAsync(Hero, None);

        await Assert.ThrowsAsync<LuaDebugStateException>(() =>
            _session.AddBreakpointAsync(Hero, 7, "Data\\Scripts\\GameObject\\Hero.lua", 30, None));

        Assert.Empty(_game.Breakpoints);
    }

    [Fact]
    public async Task AddBreakpointAsync_ThreadOnUnknownScript_Refused()
    {
        await ConnectAsync();

        await Assert.ThrowsAsync<LuaDebugStateException>(() =>
            _session.AddBreakpointAsync(-1, 2, "Data\\Scripts\\GameObject\\Hero.lua", 30, None));
    }

    [Fact]
    public async Task RemoveBreakpointAsync_SourceWide_RemovesOnTheGame()
    {
        await ConnectAsync();
        await _session.AddBreakpointAsync(-1, -1, "Hero.lua", 30, None);
        await _game.WaitForAsync<AddBreakpointMessage>();

        await _session.RemoveBreakpointAsync(-1, -1, "Hero.lua", 30, None);
        await _game.WaitForAsync<RemoveBreakpointMessage>();

        await WaitUntilAsync(() => _game.Breakpoints.Count == 0);
    }

    // -- break, suspend, continue, step -------------------------------------------------------

    [Fact]
    public async Task BreakAllAsync_WhileRunning_ArmsOnce()
    {
        await ConnectAsync();

        await _session.BreakAllAsync(None);

        Assert.Equal(DebugRunState.BreakArmed, _session.RunState);
        Assert.Equal(ArmedBreakKind.BreakAll, _session.ArmedKind);
        await _game.WaitForAsync<BreakAllMessage>();
        await WaitUntilAsync(() => _game.Armed == ArmedBreakKind.BreakAll);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task BreakAllAsync_WhileAlreadyArmed_RefusedAndNotSent()
    {
        await ConnectAsync();
        await _session.BreakAllAsync(None);

        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.BreakAllAsync(None));

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(_game.Received.OfType<BreakAllMessage>());
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task Suspended_FromTheGame_UpdatesMirrorAndRaisesEvent()
    {
        await ConnectAsync();
        ScriptSuspendedMessage? raised = null;
        _session.Suspended += m => raised = m;

        await SuspendHeroAsync();

        Assert.NotNull(raised);
        Assert.Equal(Hero, _session.SuspendedScriptId);
        Assert.Equal(Hero, _session.ContextScriptId);
        Assert.Equal(2, _session.Callstack.Count);
        Assert.Equal(1, _session.SelectedFrame);
        Assert.Equal(ArmedBreakKind.None, _session.ArmedKind);
        Assert.Contains(Hero, _session.AttachedScriptIds);
    }

    [Fact]
    public async Task ContinueAsync_WhileSuspended_ResumesAndClearsFrames()
    {
        await ConnectAsync();
        await SuspendHeroAsync();

        await _session.ContinueAsync(None);

        Assert.Equal(DebugRunState.Running, _session.RunState);
        Assert.Null(_session.SuspendedScriptId);
        Assert.Empty(_session.Callstack);
        Assert.Equal(Hero, _session.ContextScriptId);
        await _game.WaitForAsync<ContinueMessage>();
        await WaitUntilAsync(() => !_game.IsSuspended);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task ContinueAsync_WhileRunning_Refused()
    {
        await ConnectAsync();

        var e = await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.ContinueAsync(None));

        Assert.Contains("no script is suspended", e.Message);
        Assert.DoesNotContain(_game.Received, m => m is ContinueMessage);
    }

    [Fact]
    public async Task BreakAllAsync_WhileSuspended_Refused()
    {
        await ConnectAsync();
        await SuspendHeroAsync();

        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.BreakAllAsync(None));

        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task StepOverAsync_WhileSuspended_ArmsAStepThenSuspendsAgain()
    {
        await ConnectAsync();
        await SuspendHeroAsync();

        await _session.StepOverAsync(None);

        Assert.Equal(DebugRunState.BreakArmed, _session.RunState);
        Assert.Equal(ArmedBreakKind.Step, _session.ArmedKind);
        await _game.WaitForAsync<StepOverMessage>();
        await WaitUntilAsync(() => _game.Armed == ArmedBreakKind.Step);

        // A break-all on top of an armed step is accepted by the game.
        await _session.BreakAllAsync(None);
        Assert.Equal(ArmedBreakKind.BreakAll, _session.ArmedKind);
        await WaitUntilAsync(() => _game.Armed == ArmedBreakKind.BreakAll);

        await SuspendHeroAsync();
        Assert.Equal(DebugRunState.Suspended, _session.RunState);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task StepOverAsync_WhileRunning_Refused()
    {
        await ConnectAsync();

        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.StepOverAsync(None));
    }

    // -- frames -------------------------------------------------------------------------------

    [Fact]
    public async Task SelectFrameAsync_InsideTheStack_SendsDepthForTheSuspendedScript()
    {
        await ConnectAsync();
        await SuspendHeroAsync();

        await _session.SelectFrameAsync(0, None);

        Assert.Equal(0, _session.SelectedFrame);
        var sent = await _game.WaitForAsync<SetCallstackDepthMessage>();
        Assert.Equal(new SetCallstackDepthMessage(Hero, 0), sent);
        await WaitUntilAsync(() => _game.SelectedLevel == 0);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task SelectFrameAsync_OutsideTheStack_Refused()
    {
        await ConnectAsync();
        await SuspendHeroAsync();

        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.SelectFrameAsync(2, None));
        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.SelectFrameAsync(-1, None));
    }

    [Fact]
    public async Task SelectFrameAsync_WhileRunning_Refused()
    {
        await ConnectAsync();

        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.SelectFrameAsync(0, None));
    }

    // -- script and thread selection ----------------------------------------------------------

    [Fact]
    public async Task SelectScriptAsync_MakesItTheContextAndArmsABreak()
    {
        await ConnectAsync();

        await _session.SelectScriptAsync(Ai, None);

        Assert.Equal(Ai, _session.ContextScriptId);
        Assert.Equal(DebugRunState.BreakArmed, _session.RunState);
        Assert.Equal(ArmedBreakKind.BreakAll, _session.ArmedKind);
        await _game.WaitForAsync<SelectScriptMessage>();
        await WaitUntilAsync(() => _game.ContextScriptId == Ai);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task SelectScriptAsync_SameScriptTwice_SendsOnce()
    {
        await ConnectAsync();

        await _session.SelectScriptAsync(Ai, None);
        await _session.SelectScriptAsync(Ai, None);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(_game.Received.OfType<SelectScriptMessage>());
    }

    [Fact]
    public async Task BreakThreadAsync_WithoutContext_Refused()
    {
        await ConnectAsync();

        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.BreakThreadAsync(-1, None));

        Assert.DoesNotContain(_game.Received, m => m is BreakThreadMessage);
    }

    [Fact]
    public async Task BreakThreadAsync_KnownThreadOfContext_Arms()
    {
        await ConnectAsync();
        await _session.RequestThreadsAsync(Hero, None);
        await _session.SelectScriptAsync(Hero, None);

        await _session.BreakThreadAsync(2, None);

        Assert.Equal(ArmedBreakKind.BreakThread, _session.ArmedKind);
        await _game.WaitForAsync<BreakThreadMessage>();
        await WaitUntilAsync(() => _game.Armed == ArmedBreakKind.BreakThread);
        Assert.Empty(_game.Violations);
    }

    [Fact]
    public async Task BreakThreadAsync_UnknownThread_Refused()
    {
        await ConnectAsync();
        await _session.SelectScriptAsync(Hero, None);

        await Assert.ThrowsAsync<LuaDebugStateException>(() => _session.BreakThreadAsync(7, None));
    }

    // -- instance tracking --------------------------------------------------------------------

    [Fact]
    public async Task ScriptAdded_FromTheGame_JoinsTheInstancesAndRaises()
    {
        await ConnectAsync();
        ScriptEntry? added = null;
        _session.ScriptAdded += s => added = s;

        await _game.AddScriptAsync(new ScriptEntry(400, "Data\\Scripts\\GameObject\\Hero.lua"));
        await WaitUntilAsync(() => added is not null);

        Assert.Equal(new ScriptEntry(400, "Data\\Scripts\\GameObject\\Hero.lua"), added);
        Assert.Equal(3, _session.Scripts.Count);
    }

    [Fact]
    public async Task ScriptRemoved_ContextScript_ClearsExecutionState()
    {
        await ConnectAsync();
        await SuspendHeroAsync();
        var removed = new List<int>();
        _session.ScriptRemoved += id => removed.Add(id);

        await _game.RemoveScriptAsync(Hero);
        await WaitUntilAsync(() => !_session.Scripts.ContainsKey(Hero));

        Assert.Equal([Hero], removed);
        Assert.Equal(DebugRunState.Running, _session.RunState);
        Assert.Null(_session.ContextScriptId);
        Assert.Null(_session.SuspendedScriptId);
        Assert.DoesNotContain(Hero, _session.AttachedScriptIds);
    }

    [Fact]
    public async Task RequestScriptsAsync_DropsInstancesTheGameNoLongerLists()
    {
        await ConnectAsync();
        await _session.AttachAsync(Hero, None);
        _game.Scripts.RemoveAll(s => s.ScriptId == Hero);

        await _session.RequestScriptsAsync(None);

        Assert.Equal([Ai], _session.Scripts.Keys);
        Assert.Empty(_session.AttachedScriptIds);
    }

    [Fact]
    public async Task Output_FromTheGame_IsRaised()
    {
        await ConnectAsync();
        OutputMessage? output = null;
        _session.Output += m => output = m;

        await _game.OutputAsync(1, "type = number, value = 1.000000");
        await WaitUntilAsync(() => output is not null);

        Assert.Equal(1, output!.OutputType);
    }

    // -- transport behaviour ------------------------------------------------------------------

    [Fact]
    public async Task RequestScriptsAsync_FirstDatagramLost_IsResentAndAnswered()
    {
        await ConnectAsync();
        _game.DropNextGuaranteed = 1;

        var request = _session.RequestScriptsAsync(None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(2.5));

        var scripts = await request;
        Assert.Equal(2, scripts.Count);
    }

    [Fact]
    public async Task ScriptList_LargerThanOneDatagram_IsReassembled()
    {
        for (var i = 0; i < 120; i++)
            _game.Scripts.Add(new ScriptEntry(1000 + i, $"Data\\Scripts\\GameObject\\Instance_{i:000}.lua"));
        await ConnectAsync();

        Assert.Equal(122, _session.Scripts.Count);
        Assert.Equal("Data\\Scripts\\GameObject\\Instance_119.lua", _session.Scripts[1119].FullPathName);
    }

    [Fact]
    public async Task Silence_LongerThanTheWatchdog_LosesTheConnection()
    {
        await ConnectAsync();
        Exception? lost = null;
        _session.ConnectionLost += e => lost = e;

        _time.Advance(TimeSpan.FromSeconds(16));
        await WaitUntilAsync(() => lost is not null);

        Assert.IsType<LuaDebugConnectionLostException>(lost);
        Assert.False(_session.IsConnected);
        await Assert.ThrowsAsync<LuaDebugConnectionLostException>(() => _session.RequestScriptsAsync(None));
    }

    [Fact]
    public async Task Heartbeats_KeepTheWatchdogQuiet()
    {
        await ConnectAsync();
        Exception? lost = null;
        _session.ConnectionLost += e => lost = e;

        for (var i = 0; i < 4; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(5));
            await _game.HeartbeatAsync();
            await Task.Delay(30, TestContext.Current.CancellationToken);
        }

        Assert.Null(lost);
        Assert.True(_session.IsConnected);
    }

    [Fact]
    public async Task Goodbye_FromTheGame_LosesTheConnection()
    {
        await ConnectAsync();
        Exception? lost = null;
        _session.ConnectionLost += e => lost = e;

        await _game.GoodbyeAsync();
        await WaitUntilAsync(() => lost is not null);

        Assert.Contains("closed", lost!.Message);
    }

    [Fact]
    public async Task DisconnectAsync_SaysGoodbyeAndTheGameForgetsBreakpoints()
    {
        await ConnectAsync();
        await _session.AddBreakpointAsync(-1, -1, "Hero.lua", 30, None);
        await _game.WaitForAsync<AddBreakpointMessage>();

        await _session.DisconnectAsync(None);

        await WaitUntilAsync(() => _game.GoodbyeReceived);
        Assert.Empty(_game.Breakpoints);
        Assert.False(_session.IsConnected);
        Assert.Empty(_session.Scripts);
    }
}