// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.DebugAdapter.Client;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using PG.StarWarsGame.LSP.Lua.Debug.Dap;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;
using PG.StarWarsGame.LSP.Lua.Debug.Tests.Session;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The debug adapter as VS Code runs it: the server binary started with <c>--debug-adapter</c>,
///     spoken to over its stdio with the OmniSharp DAP client, against the fake game on a loopback
///     UDP port. One full session: breakpoint before attach, attach, stop, stack, locals, continue,
///     the scripts view's request, disconnect - and not one packet the engine would assert on.
/// </summary>
[Trait("Category", "E2E")]
public sealed class LuaDebugAdapterSmokeTest : IAsyncLifetime
{
    private const int Hero = 343;
    private const string HeroGamePath = @"Data\Scripts\GameObject\Hero.lua";

    private const string HeroScript = """
                                      function Tick(unit, dt)
                                          local planet = unit.Get_Planet()
                                          local speed = 3
                                          Move(planet, speed)
                                          return speed
                                      end
                                      """;

    private static readonly TimeSpan Step = TimeSpan.FromSeconds(20);

    private readonly FakeGameDebugServer _game = new();
    private readonly Lock _gate = new();
    private readonly List<StoppedEvent> _stopped = [];
    private readonly List<BreakpointEvent> _breakpoints = [];
    private readonly List<TerminatedEvent> _terminated = [];
    private string _root = null!;
    private string _heroPath = null!;
    private Process _process = null!;
    private DebugAdapterClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "aet-lua-debug-e2e-" + Guid.NewGuid().ToString("N"));
        _heroPath = Path.Combine(_root, "Data", "Scripts", "GameObject", "Hero.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(_heroPath)!);
        await File.WriteAllTextAsync(_heroPath, HeroScript);

        _game.Scripts.Add(new ScriptEntry(Hero, HeroGamePath));
        _game.Threads[Hero] = [new ThreadEntry(0, "main")];
        _game.Variables[(Hero, "unit")] = (LuaTypeNames.Userdata, "userdata: 0x1");
        _game.Variables[(Hero, "dt")] = (LuaTypeNames.Number, "0");
        _game.Variables[(Hero, "planet")] = (LuaTypeNames.Table, "table: 0x0A1B2C3D");
        _game.Variables[(Hero, "speed")] = (LuaTypeNames.Number, "3");

        _process = StartAdapter();
        _client = DebugAdapterClient.Create(options => options
            .WithInput(PipeReader.Create(_process.StandardOutput.BaseStream))
            .WithOutput(PipeWriter.Create(_process.StandardInput.BaseStream))
            .WithLoggerFactory(NullLoggerFactory.Instance)
            .OnStopped(e => Record(_stopped, e))
            .OnBreakpoint(e => Record(_breakpoints, e))
            .OnTerminated(e => Record(_terminated, e)));
        using var cts = new CancellationTokenSource(Step);
        await _client.Initialize(cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill();
        }

        await _process.WaitForExitAsync();
        _process.Dispose();
        await _game.DisposeAsync();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A late file watcher can hold the folder for a moment; a temp folder left behind is not a failure.
        }
    }

    [Fact]
    public async Task DebugAdapter_OverStdio_RunsAWholeSessionAgainstTheFakeGame()
    {
        // A breakpoint set before attach waits, then is announced once the game has it.
        var pending = await SendAsync<SetBreakpointsResponse>("setBreakpoints", new SetBreakpointsArguments
        {
            Source = new Source { Path = _heroPath, Name = "Hero.lua" },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 4 })
        });
        var waiting = Assert.Single(pending.Breakpoints);
        Assert.False(waiting.Verified);

        await SendAsync<AttachResponse>("attach", new
        {
            host = _game.EndPoint.Address.ToString(),
            port = _game.EndPoint.Port,
            sourceRoots = new[] { _root },
            clientName = "AetLuaDebugger:e2e"
        });
        await SendAsync<ConfigurationDoneResponse>("configurationDone", new ConfigurationDoneArguments());

        var announced = await WaitForAsync(() => Snapshot(_breakpoints));
        Assert.True(announced.Breakpoint.Verified);
        Assert.Equal(waiting.Id, announced.Breakpoint.Id);
        Assert.Contains(_game.Breakpoints, b => b.SourceName == HeroGamePath && b.Line == 4);
        Assert.Equal("AetLuaDebugger:e2e", _game.ClientName);

        // The game stops on that line.
        await _game.SuspendAsync(Hero, -1,
            [$"{HeroGamePath}:1:main::", $"{HeroGamePath}:4:Lua:global:Tick"], _game.Threads[Hero]);
        var stopped = await WaitForAsync(() => Snapshot(_stopped));
        Assert.Equal(StoppedEventReason.Breakpoint, stopped.Reason);
        Assert.Equal(Hero + 2, stopped.ThreadId);

        var threads = await SendAsync<ThreadsResponse>("threads", new ThreadsArguments());
        var thread = Assert.Single(threads.Threads!);
        Assert.Equal("Hero.lua [343]", thread.Name);

        var stack = await SendAsync<StackTraceResponse>("stackTrace", new StackTraceArguments { ThreadId = thread.Id });
        Assert.Equal(2, stack.StackFrames!.Count());
        var top = stack.StackFrames!.First();
        Assert.Equal(4, top.Line);
        Assert.Equal(_heroPath, top.Source!.Path, ignoreCase: true);

        var scopes = await SendAsync<ScopesResponse>("scopes", new ScopesArguments { FrameId = top.Id });
        var locals = Assert.Single(scopes.Scopes);
        Assert.Equal("Locals", locals.Name);

        var variables = await SendAsync<VariablesResponse>("variables",
            new VariablesArguments { VariablesReference = locals.VariablesReference });
        var names = variables.Variables!.ToDictionary(v => v.Name, v => v.Value);
        Assert.Equal("3", names["speed"]);
        Assert.Equal("table: 0x0A1B2C3D", names["planet"]);

        var resumed = await SendAsync<ContinueResponse>("continue", new ContinueArguments { ThreadId = thread.Id });
        Assert.True(resumed.AllThreadsContinued);
        await WaitUntilAsync(() => !_game.IsSuspended);

        // The scripts view's own request travels the same wire.
        var scripts = await SendAsync<LuaScriptsResult>(LuaCustomMessages.Scripts, new { });
        Assert.Equal(LuaRunStates.Running, scripts.RunState);
        var script = Assert.Single(scripts.Scripts);
        Assert.Equal(Hero, script.ScriptId);
        Assert.Equal(_heroPath, script.Path, ignoreCase: true);

        await SendAsync<DisconnectResponse>("disconnect", new DisconnectArguments());
        await WaitUntilAsync(() => _game.GoodbyeReceived);
        await WaitUntilAsync(() => _process.HasExited);

        Assert.Empty(_game.Violations);
        Assert.Empty(Snapshot(_terminated));
    }

    // -- helpers ------------------------------------------------------------------------------

    private static Process StartAdapter()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(LuaDebugAdapterSmokeTest).Assembly.Location)!;
        var serverDll = Path.Combine(assemblyDir, "PG.StarWarsGame.LSP.Server.dll");
        var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{serverDll}\" --debug-adapter")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                    Console.Error.WriteLine($"[DAP] {line}");
            }
            catch
            {
                // process exited
            }
        });
        return process;
    }

    private async Task<T> SendAsync<T>(string command, object arguments)
    {
        using var cts = new CancellationTokenSource(Step);
        return await _client.SendRequest(command, arguments).Returning<T>(cts.Token);
    }

    private void Record<T>(List<T> list, T item)
    {
        lock (_gate)
        {
            list.Add(item);
        }
    }

    private IReadOnlyList<T> Snapshot<T>(List<T> list)
    {
        lock (_gate)
        {
            return list.ToArray();
        }
    }

    private static async Task<T> WaitForAsync<T>(Func<IReadOnlyList<T>> events)
    {
        using var deadline = new CancellationTokenSource(Step);
        while (true)
        {
            var current = events();
            if (current.Count > 0)
                return current[^1];
            await Task.Delay(20, deadline.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(Step);
        while (!condition())
            await Task.Delay(20, deadline.Token);
    }
}
