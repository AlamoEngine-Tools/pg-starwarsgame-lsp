// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OmniSharp.Extensions.DebugAdapter.Client;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Server;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Lua.Debug.Dap;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.Dap;

/// <summary>
///     The adapter as a real DAP server on in-memory pipes, driven by the OmniSharp DAP client,
///     against the fake game. Every event the server sends is recorded for the tests to wait on.
/// </summary>
internal sealed class DapTestHarness : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly List<StoppedEvent> _stopped = [];
    private readonly List<OutputEvent> _output = [];
    private readonly List<TerminatedEvent> _terminated = [];
    private readonly List<InitializedEvent> _initialized = [];
    private readonly List<BreakpointEvent> _breakpointEvents = [];
    private readonly List<LuaScriptsChangedEvent> _scriptsChanged = [];

    private DapTestHarness(FakeTimeProvider time, MockFileSystem fileSystem, FakeGameLauncher launcher,
        IServiceProvider services)
    {
        Time = time;
        FileSystem = fileSystem;
        Launcher = launcher;
        Services = services;
        Adapter = services.GetRequiredService<LuaDebugAdapter>();
    }

    public FakeTimeProvider Time { get; }

    public MockFileSystem FileSystem { get; }

    public FakeGameLauncher Launcher { get; }

    public IServiceProvider Services { get; }

    public LuaDebugAdapter Adapter { get; }

    public DebugAdapterServer Server { get; private set; } = null!;

    public DebugAdapterClient Client { get; private set; } = null!;

    public IReadOnlyList<StoppedEvent> Stopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped.ToArray();
            }
        }
    }

    public IReadOnlyList<OutputEvent> Output
    {
        get
        {
            lock (_gate)
            {
                return _output.ToArray();
            }
        }
    }

    public IReadOnlyList<TerminatedEvent> Terminated
    {
        get
        {
            lock (_gate)
            {
                return _terminated.ToArray();
            }
        }
    }

    public IReadOnlyList<InitializedEvent> Initialized
    {
        get
        {
            lock (_gate)
            {
                return _initialized.ToArray();
            }
        }
    }

    public IReadOnlyList<BreakpointEvent> BreakpointEvents
    {
        get
        {
            lock (_gate)
            {
                return _breakpointEvents.ToArray();
            }
        }
    }

    /// <summary>The custom <c>eawLua/scriptsChanged</c> events, which the scripts view follows.</summary>
    public IReadOnlyList<LuaScriptsChangedEvent> ScriptsChanged
    {
        get
        {
            lock (_gate)
            {
                return _scriptsChanged.ToArray();
            }
        }
    }

    /// <summary>Builds the adapter's container, starts the server and the client, and completes the initialize handshake.</summary>
    public static async Task<DapTestHarness> StartAsync(Action<MockFileSystem>? files = null,
        CancellationToken cancellationToken = default)
    {
        var time = new FakeTimeProvider();
        var fileSystem = new MockFileSystem();
        files?.Invoke(fileSystem);
        var launcher = new FakeGameLauncher();
        var services = TestServices.Build(s =>
        {
            s.AddSingleton<TimeProvider>(time);
            s.AddSingleton<IFileSystem>(fileSystem);
            s.AddSingleton<IGameLauncher>(launcher);
        });
        var harness = new DapTestHarness(time, fileSystem, launcher, services);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var serverTask = LuaDebugAdapterHost.StartAsync(clientToServer.Reader, serverToClient.Writer, harness.Adapter,
            NullLoggerFactory.Instance, cancellationToken);
        var tapped = harness.Tap(serverToClient.Reader);
        var client = DebugAdapterClient.Create(options => options
            .WithInput(tapped)
            .WithOutput(clientToServer.Writer)
            .WithLoggerFactory(NullLoggerFactory.Instance)
            .OnStopped(e => harness.Record(harness._stopped, e))
            .OnOutput(e => harness.Record(harness._output, e))
            .OnTerminated(e => harness.Record(harness._terminated, e))
            .OnDebugAdapterInitialized(e => harness.Record(harness._initialized, e))
            .OnBreakpoint(e => harness.Record(harness._breakpointEvents, e))
            .OnJsonNotification(LuaCustomMessages.ScriptsChanged,
                body => harness.Record(harness._scriptsChanged, body.ToObject<LuaScriptsChangedEvent>()!)));
        await client.Initialize(cancellationToken);
        harness.Client = client;
        harness.Server = await serverTask;
        return harness;
    }

    public async Task<T> WaitForAsync<T>(Func<IReadOnlyList<T>> events, int count = 1, int timeoutMilliseconds = 5000)
    {
        using var deadline = new CancellationTokenSource(timeoutMilliseconds);
        while (true)
        {
            var current = events();
            if (current.Count >= count)
                return current[count - 1];
            await Task.Delay(10, deadline.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Server.Dispose();
        Adapter.Dispose();
        await Task.CompletedTask;
    }

    private void Record<T>(List<T> list, T item)
    {
        lock (_gate)
        {
            list.Add(item);
        }
    }

    /// <summary>
    ///     The message of the last failed response the server wrote. The OmniSharp test client maps
    ///     the adapter's error code to an unrelated exception type, so tests read the wire, which is
    ///     what VS Code shows the user.
    /// </summary>
    public string? LastErrorMessage()
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(ServerOutput,
            "\"success\":false,\"command\":\"[^\"]+\",\"message\":\"((?:[^\"\\\\]|\\\\.)*)\"");
        return matches.Count == 0 ? null : System.Text.RegularExpressions.Regex.Unescape(matches[^1].Groups[1].Value);
    }

    /// <summary>Everything the server has written, as text, for assertions on the wire itself.</summary>
    public string ServerOutput
    {
        get
        {
            lock (_gate)
            {
                return _serverOutput.ToString();
            }
        }
    }

    private readonly System.Text.StringBuilder _serverOutput = new();

    /// <summary>Copies the server's output into a second pipe for the client while recording it.</summary>
    private PipeReader Tap(PipeReader source)
    {
        var copy = new Pipe();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var result = await source.ReadAsync();
                foreach (var segment in result.Buffer)
                {
                    lock (_gate)
                    {
                        _serverOutput.Append(System.Text.Encoding.UTF8.GetString(segment.Span));
                    }

                    await copy.Writer.WriteAsync(segment);
                }

                source.AdvanceTo(result.Buffer.End);
                if (result.IsCompleted)
                {
                    await copy.Writer.CompleteAsync();
                    return;
                }
            }
        });
        return copy.Reader;
    }
}