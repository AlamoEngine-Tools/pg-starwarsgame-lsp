// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Protocol.Server;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;
using PG.StarWarsGame.LSP.Lua.Debug.Session;
using PG.StarWarsGame.LSP.Lua.Debug.Sources;
using Thread = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread;

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <summary>
///     The Debug Adapter Protocol face of one session with the game. Threads are the suspended
///     script, or one stable "Game" thread while nothing is suspended; the full script list belongs
///     to the scripts view, not to the thread list. File breakpoints become source-wide game
///     breakpoints. A frame's Locals scope lists the names the source parse finds in scope and asks
///     the game for each; table values expand only when the configuration allows it.
/// </summary>
public sealed partial class LuaDebugAdapter : IDisposable
{
    /// <summary>The thread shown while no script is suspended, so pause has a target.</summary>
    public const long GameThreadId = 1;

    /// <summary>Frame ids carry the stop they belong to, so a stale frame from a previous stop is refused.</summary>
    private const long FramesPerStop = 1024;

    private readonly ILuaDebugSessionFactory _sessions;
    private readonly IScriptSourceMapFactory _maps;
    private readonly ICallstackParser _callstacks;
    private readonly IFrameLocalsProvider _locals;
    private readonly IFileHelper _fileHelper;
    private readonly IGameLauncher _launcher;
    private readonly TimeProvider _time;
    private readonly ILogger<LuaDebugAdapter> _logger;
    private readonly Lock _gate = new();

    /// <summary>Breakpoints by document URI and line; each carries the id the client knows it by and whether the game has it.</summary>
    private readonly Dictionary<string, Dictionary<int, BreakpointEntry>> _breakpoints = new(DocumentUris.Comparer);

    private readonly Dictionary<long, object> _handles = [];
    private long _nextBreakpointId = 1;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IDebugAdapterServer? _server;
    private ILuaDebugSession? _session;
    private IScriptSourceMap _map;
    private IGameProcess? _process;
    private bool _unsafeTableExpansion;
    private bool _dropDuplicateOutermost = true;
    private long _stopSequence;
    private long _nextHandle = 1;
    private IReadOnlyList<CallstackFrame> _frames = [];
    private StoppedEventReason? _pendingReason;

    public LuaDebugAdapter(
        ILuaDebugSessionFactory sessions,
        IScriptSourceMapFactory maps,
        ICallstackParser callstacks,
        IFrameLocalsProvider locals,
        IFileHelper fileHelper,
        IGameLauncher launcher,
        TimeProvider time,
        ILogger<LuaDebugAdapter> logger)
    {
        _sessions = sessions;
        _maps = maps;
        _callstacks = callstacks;
        _locals = locals;
        _fileHelper = fileHelper;
        _launcher = launcher;
        _time = time;
        _logger = logger;
        _map = maps.Create([]);
    }

    /// <summary>Completes once the client has disconnected or asked for termination.</summary>
    public Task Exited => _exited.Task;

    public static InitializeResponse Capabilities => new()
    {
        SupportsConfigurationDoneRequest = true,
        SupportsEvaluateForHovers = true,
        SupportsLoadedSourcesRequest = true,
        SupportsTerminateRequest = true,
        SupportTerminateDebuggee = true,
        // The game stores a breakpoint condition and never evaluates it.
        SupportsConditionalBreakpoints = false,
        SupportsHitConditionalBreakpoints = false,
        SupportsLogPoints = false,
        SupportsSetVariable = false,
        SupportsRestartRequest = false,
        SupportsDelayedStackTraceLoading = false,
        ExceptionBreakpointFilters = new Container<ExceptionBreakpointsFilter>()
    };

    /// <summary>Gives the adapter the server it sends events through. Called once the server is up.</summary>
    public void Bind(IDebugAdapterServer server)
    {
        _server = server;
    }

    public void Dispose()
    {
        _process?.Dispose();
    }

    // -- lifecycle ----------------------------------------------------------------------------

    public async Task<AttachResponse> AttachAsync(LuaAttachArguments args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = PrepareSession(args);
        await session.ConnectAsync(EndPoint(args), ClientName(args), cancellationToken).ConfigureAwait(false);
        await ConnectedAsync(session, cancellationToken).ConfigureAwait(false);
        return new AttachResponse();
    }

    public async Task<LaunchResponse> LaunchAsync(LuaLaunchArguments args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (string.IsNullOrWhiteSpace(args.Program))
            throw new LuaDebugAdapterException("The launch configuration names no game executable ('program')");

        _process = _launcher.Start(args.Program, args.Args ?? [], args.Cwd);
        _logger.LogInformation("Started {Program} as process {Id}", args.Program, _process.Id);

        var deadline = _time.GetTimestamp();
        var timeout = TimeSpan.FromSeconds(Math.Max(1, args.AttachTimeoutSeconds));
        while (true)
        {
            var session = PrepareSession(args);
            try
            {
                await session.ConnectAsync(EndPoint(args), ClientName(args), cancellationToken).ConfigureAwait(false);
                await ConnectedAsync(session, cancellationToken).ConfigureAwait(false);
                return new LaunchResponse();
            }
            catch (Exception e) when (e is TimeoutException or LuaDebugConnectionLostException)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                if (_process.HasExited)
                    throw new LuaDebugAdapterException(
                        $"The game exited with code {_process.ExitCode} before the debugger could attach", e);
                if (_time.GetElapsedTime(deadline) >= timeout)
                    throw new LuaDebugAdapterException(
                        $"The game did not answer on {args.Host}:{args.Port} within {timeout.TotalSeconds:0} s. " +
                        "Is it a debug build with the Lua debug server started (console command luadebug)?", e);
                await Task.Delay(TimeSpan.FromSeconds(1), _time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task<ConfigurationDoneResponse> ConfigurationDoneAsync(ConfigurationDoneArguments args,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new ConfigurationDoneResponse());
    }

    public async Task<DisconnectResponse> DisconnectAsync(DisconnectArguments args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        await EndSessionAsync(args.TerminateDebuggee, cancellationToken).ConfigureAwait(false);
        _exited.TrySetResult();
        return new DisconnectResponse();
    }

    public async Task<TerminateResponse> TerminateAsync(TerminateArguments args, CancellationToken cancellationToken)
    {
        await EndSessionAsync(true, cancellationToken).ConfigureAwait(false);
        _server?.SendTerminated(new TerminatedEvent());
        _exited.TrySetResult();
        return new TerminateResponse();
    }

    // -- threads and frames -------------------------------------------------------------------

    public Task<ThreadsResponse> ThreadsAsync(ThreadsArguments args, CancellationToken cancellationToken)
    {
        var session = _session;
        Thread thread;
        if (session is { RunState: DebugRunState.Suspended, SuspendedScriptId: { } scriptId })
        {
            var name = session.Scripts.TryGetValue(scriptId, out var script)
                ? $"{FileName(script.FullPathName)} [{scriptId}]"
                : $"script {scriptId}";
            thread = new Thread { Id = ThreadIdFor(scriptId), Name = name };
        }
        else
        {
            thread = new Thread { Id = GameThreadId, Name = "Game" };
        }

        return Task.FromResult(new ThreadsResponse { Threads = new Container<Thread>(thread) });
    }

    public Task<StackTraceResponse> StackTraceAsync(StackTraceArguments args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = RequireSession();
        IReadOnlyList<CallstackFrame> frames;
        long sequence;
        lock (_gate)
        {
            frames = _frames;
            sequence = _stopSequence;
        }

        if (session.SuspendedScriptId is not { } scriptId || args.ThreadId != ThreadIdFor(scriptId))
            return Task.FromResult(
                new StackTraceResponse { StackFrames = new Container<StackFrame>(), TotalFrames = 0 });

        var start = (int)(args.StartFrame ?? 0);
        var count = args.Levels is > 0 ? (int)args.Levels.Value : frames.Count;
        var page = frames.Skip(start).Take(count).Select(frame => new StackFrame
        {
            Id = sequence * FramesPerStop + frame.Level,
            Name = frame.DisplayName,
            Line = frame.Line,
            Column = 1,
            Source = SourceFor(frame.Source)
        });

        return Task.FromResult(new StackTraceResponse
        {
            StackFrames = new Container<StackFrame>(page),
            TotalFrames = frames.Count
        });
    }

    public async Task<ScopesResponse> ScopesAsync(ScopesArguments args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = RequireSession();
        var frame = FrameFor(args.FrameId);
        var scriptId = session.SuspendedScriptId ?? throw new LuaDebugAdapterException("No script is suspended");

        // Selecting the frame is what makes the game resolve names against that frame's locals.
        await session.SelectFrameAsync(frame.Level, cancellationToken).ConfigureAwait(false);

        var handle = NewHandle(new LocalsHandle(scriptId, frame));
        return new ScopesResponse
        {
            Scopes = new Container<Scope>(new Scope
            {
                Name = "Locals",
                PresentationHint = "locals",
                VariablesReference = handle,
                Expensive = false
            })
        };
    }

    public async Task<VariablesResponse> VariablesAsync(VariablesArguments args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = RequireSession();
        object handle;
        lock (_gate)
        {
            if (!_handles.TryGetValue(args.VariablesReference, out handle!))
                throw new LuaDebugAdapterException("That variable reference belongs to an earlier stop");
        }

        var variables = handle switch
        {
            LocalsHandle locals => await LocalsAsync(session, locals, cancellationToken).ConfigureAwait(false),
            TableHandle table => await TableAsync(session, table, cancellationToken).ConfigureAwait(false),
            _ => throw new LuaDebugAdapterException("Unknown variable reference")
        };
        return new VariablesResponse { Variables = new Container<Variable>(variables) };
    }

    public async Task<EvaluateResponse> EvaluateAsync(EvaluateArguments args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = RequireSession();
        var scriptId = session.SuspendedScriptId ?? session.ContextScriptId
            ?? throw new LuaDebugAdapterException("Select a script or stop in one before evaluating");
        var expression = args.Expression?.Trim() ?? string.Empty;

        if (args.Context is { } context && context.Equals(EvaluateArgumentsContext.Repl))
        {
            var text = await session.ExecuteTextAsync(scriptId, expression, cancellationToken).ConfigureAwait(false);
            return new EvaluateResponse { Result = TrimPrompt(text) };
        }

        if (!IdentifierPattern().IsMatch(expression))
            throw new LuaDebugAdapterException(
                "Only a plain variable name can be read from the game; use the debug console to run Lua");

        var value = await session.DumpVariableAsync(scriptId, expression, cancellationToken).ConfigureAwait(false);
        return new EvaluateResponse
        {
            Result = value.ValueText,
            Type = LuaTypeNames.Of(value.ValueType),
            VariablesReference = TableReference(scriptId, expression, [], value.ValueType)
        };
    }

    // -- breakpoints --------------------------------------------------------------------------

    public async Task<SetBreakpointsResponse> SetBreakpointsAsync(SetBreakpointsArguments args,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var requested = args.Breakpoints?.ToList() ?? [];
        var path = args.Source?.Path;
        if (string.IsNullOrEmpty(path))
            return Unverified(requested, "The source has no file path");

        var uri = _fileHelper.NormalizeUri(path);
        var session = _session;
        var connected = session is not null && session.IsConnected;
        var gamePath = connected ? _map.ToGamePath(uri) : null;
        if (connected && gamePath is null)
            return Unverified(requested, "The file is under none of the configured script roots");

        Dictionary<int, BreakpointEntry> current;
        lock (_gate)
        {
            if (!_breakpoints.TryGetValue(uri, out current!))
            {
                current = [];
                _breakpoints[uri] = current;
            }
        }

        var results = new List<Breakpoint>(requested.Count);
        var wanted = new HashSet<int>();
        foreach (var breakpoint in requested)
        {
            if (!string.IsNullOrEmpty(breakpoint.Condition) || !string.IsNullOrEmpty(breakpoint.HitCondition)
                                                            || !string.IsNullOrEmpty(breakpoint.LogMessage))
            {
                results.Add(new Breakpoint
                {
                    Verified = false,
                    Line = breakpoint.Line,
                    Source = args.Source,
                    Message =
                        "The game does not evaluate breakpoint conditions, hit counts or log messages; the breakpoint was not set"
                });
                continue;
            }

            wanted.Add(breakpoint.Line);
            BreakpointEntry entry;
            lock (_gate)
            {
                if (!current.TryGetValue(breakpoint.Line, out entry!))
                {
                    entry = new BreakpointEntry(_nextBreakpointId++, breakpoint.Line);
                    current[breakpoint.Line] = entry;
                }
            }

            results.Add(connected
                ? new Breakpoint { Id = entry.Id, Verified = true, Line = breakpoint.Line, Source = args.Source }
                : new Breakpoint
                {
                    Id = entry.Id,
                    Verified = false,
                    Line = breakpoint.Line,
                    Source = args.Source,
                    Message = "Waiting for the game; the breakpoint is set once the debugger is attached"
                });
        }

        List<BreakpointEntry> dropped;
        lock (_gate)
        {
            dropped = current.Values.Where(e => !wanted.Contains(e.Line)).ToList();
            foreach (var entry in dropped)
                current.Remove(entry.Line);
        }

        if (!connected)
            return new SetBreakpointsResponse { Breakpoints = new Container<Breakpoint>(results) };

        foreach (var entry in dropped.Where(e => e.Applied))
            await session!.RemoveBreakpointAsync(-1, -1, gamePath!, entry.Line, cancellationToken)
                .ConfigureAwait(false);

        foreach (var entry in current.Values.Where(e => !e.Applied).ToList())
        {
            await session!.AddBreakpointAsync(-1, -1, gamePath!, entry.Line, cancellationToken).ConfigureAwait(false);
            entry.Applied = true;
        }

        return new SetBreakpointsResponse { Breakpoints = new Container<Breakpoint>(results) };
    }

    /// <summary>
    ///     Sends every breakpoint set before the session existed and tells the client which ones
    ///     the game now has. The client sets breakpoints as soon as it sees the initialized event,
    ///     which comes before attach completes.
    /// </summary>
    private async Task ApplyPendingBreakpointsAsync(ILuaDebugSession session, CancellationToken cancellationToken)
    {
        List<(string Uri, List<BreakpointEntry> Entries)> pending;
        lock (_gate)
        {
            pending = _breakpoints
                .Select(pair => (pair.Key, pair.Value.Values.Where(e => !e.Applied).ToList()))
                .Where(pair => pair.Item2.Count > 0)
                .ToList();
        }

        foreach (var (uri, entries) in pending)
        {
            var gamePath = _map.ToGamePath(uri);
            foreach (var entry in entries)
            {
                if (gamePath is null)
                {
                    Announce(entry, false, "The file is under none of the configured script roots");
                    continue;
                }

                await session.AddBreakpointAsync(-1, -1, gamePath, entry.Line, cancellationToken).ConfigureAwait(false);
                entry.Applied = true;
                Announce(entry, true, null);
            }
        }
    }

    private void Announce(BreakpointEntry entry, bool verified, string? message)
    {
        _server?.SendBreakpoint(new BreakpointEvent
        {
            Reason = BreakpointEventReason.Changed,
            Breakpoint = new Breakpoint { Id = entry.Id, Verified = verified, Line = entry.Line, Message = message }
        });
    }

    // -- execution control --------------------------------------------------------------------

    public async Task<ContinueResponse> ContinueAsync(ContinueArguments args, CancellationToken cancellationToken)
    {
        await RequireSession().ContinueAsync(cancellationToken).ConfigureAwait(false);
        ClearStop();
        return new ContinueResponse { AllThreadsContinued = true };
    }

    public async Task<NextResponse> NextAsync(NextArguments args, CancellationToken cancellationToken)
    {
        await RequireSession().StepOverAsync(cancellationToken).ConfigureAwait(false);
        ClearStop(StoppedEventReason.Step);
        return new NextResponse();
    }

    public async Task<StepInResponse> StepInAsync(StepInArguments args, CancellationToken cancellationToken)
    {
        await RequireSession().StepIntoAsync(cancellationToken).ConfigureAwait(false);
        ClearStop(StoppedEventReason.Step);
        return new StepInResponse();
    }

    public async Task<StepOutResponse> StepOutAsync(StepOutArguments args, CancellationToken cancellationToken)
    {
        await RequireSession().StepOutAsync(cancellationToken).ConfigureAwait(false);
        ClearStop(StoppedEventReason.Step);
        return new StepOutResponse();
    }

    public async Task<PauseResponse> PauseAsync(PauseArguments args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = RequireSession();
        if (args.ThreadId == GameThreadId)
        {
            lock (_gate)
            {
                _pendingReason = StoppedEventReason.Pause;
            }

            await session.BreakAllAsync(cancellationToken).ConfigureAwait(false);
            NotifyScriptsChanged(LuaScriptsChangedReasons.StateChanged);
            return new PauseResponse();
        }

        await ArmBreakInAsync(session, ScriptIdFor(args.ThreadId), cancellationToken).ConfigureAwait(false);
        return new PauseResponse();
    }

    // -- the scripts view ---------------------------------------------------------------------

    /// <summary>The script list as the adapter knows it; nothing is asked of the game.</summary>
    public Task<LuaScriptsResult> ScriptsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Snapshot(RequireSession()));
    }

    /// <summary>Reloads one script's threads, or the whole script list, from the game.</summary>
    public async Task<LuaScriptsResult> RefreshAsync(LuaRefreshArguments? args, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        if (args?.ScriptId is { } scriptId)
            await session.RequestThreadsAsync(scriptId, cancellationToken).ConfigureAwait(false);
        else
            await session.RequestScriptsAsync(cancellationToken).ConfigureAwait(false);
        return Snapshot(session);
    }

    /// <summary>Arms a break at the next Lua line of the script; the game stops there and reports a pause.</summary>
    public async Task<LuaScriptsResult> SelectScriptAsync(LuaSelectScriptArguments args,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = RequireSession();
        await ArmBreakInAsync(session, args.ScriptId, cancellationToken).ConfigureAwait(false);
        return Snapshot(session);
    }

    /// <summary>
    ///     Arms a break at the next Lua line of one coroutine thread. The thread is checked against
    ///     the game's thread list first, so a stale row refuses before anything is armed.
    /// </summary>
    public async Task<LuaScriptsResult> BreakThreadAsync(LuaBreakThreadArguments args,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = RequireSession();
        var threads = session.ThreadsByScript.TryGetValue(args.ScriptId, out var known)
            ? known
            : await session.RequestThreadsAsync(args.ScriptId, cancellationToken).ConfigureAwait(false);
        if (args.ThreadIndex != -1 && threads.All(t => t.ThreadIndex != args.ThreadIndex))
            throw new LuaDebugAdapterException(
                $"Thread {args.ThreadIndex} is not a thread of script {args.ScriptId} any more; refresh its threads");

        lock (_gate)
        {
            _pendingReason = StoppedEventReason.Pause;
        }

        if (session.ContextScriptId != args.ScriptId)
            await session.SelectScriptAsync(args.ScriptId, cancellationToken).ConfigureAwait(false);
        await session.BreakThreadAsync(args.ThreadIndex, cancellationToken).ConfigureAwait(false);
        NotifyScriptsChanged(LuaScriptsChangedReasons.StateChanged);
        return Snapshot(session);
    }

    /// <summary>
    ///     Selecting a script arms a break in it; selecting the script that is already the
    ///     context would be a no-op on the game, so that case arms a plain break instead.
    /// </summary>
    private async Task ArmBreakInAsync(ILuaDebugSession session, int scriptId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _pendingReason = StoppedEventReason.Pause;
        }

        if (session.ContextScriptId == scriptId)
            await session.BreakAllAsync(cancellationToken).ConfigureAwait(false);
        else
            await session.SelectScriptAsync(scriptId, cancellationToken).ConfigureAwait(false);
        NotifyScriptsChanged(LuaScriptsChangedReasons.StateChanged);
    }

    private LuaScriptsResult Snapshot(ILuaDebugSession session)
    {
        var attached = session.AttachedScriptIds;
        var threads = session.ThreadsByScript;
        var context = session.ContextScriptId;
        var suspended = session.SuspendedScriptId;
        var rows = session.Scripts.Values
            .OrderBy(s => FileName(s.FullPathName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.ScriptId)
            .Select(s => new LuaScriptRow(
                s.ScriptId,
                s.FullPathName,
                FileName(s.FullPathName),
                SourceFor(s.FullPathName).Path,
                attached.Contains(s.ScriptId),
                context == s.ScriptId,
                suspended == s.ScriptId,
                threads.TryGetValue(s.ScriptId, out var known)
                    ? known.Select(t => new LuaThreadRow(t.ThreadIndex, t.Name)).ToList()
                    : null))
            .ToList();
        return new LuaScriptsResult(RunStateName(session.RunState), rows);
    }

    private static string RunStateName(DebugRunState state)
    {
        return state switch
        {
            DebugRunState.Suspended => LuaRunStates.Suspended,
            DebugRunState.BreakArmed => LuaRunStates.BreakArmed,
            _ => LuaRunStates.Running
        };
    }

    private void NotifyScriptsChanged(string reason, int? scriptId = null)
    {
        _server?.SendNotification(LuaCustomMessages.ScriptsChanged, new LuaScriptsChangedEvent(reason, scriptId));
    }

    // -- sources ------------------------------------------------------------------------------

    public Task<LoadedSourcesResponse> LoadedSourcesAsync(LoadedSourcesArguments args,
        CancellationToken cancellationToken)
    {
        var scripts = _session?.Scripts.Values ?? [];
        var sources = new List<Source>();
        var seen = new HashSet<string>(DocumentUris.Comparer);
        foreach (var script in scripts)
        {
            var source = SourceFor(script.FullPathName);
            var key = source.Path ?? source.Name ?? script.FullPathName;
            if (seen.Add(key))
                sources.Add(source);
        }

        return Task.FromResult(new LoadedSourcesResponse { Sources = new Container<Source>(sources) });
    }

    // -- session events -----------------------------------------------------------------------

    private void OnSuspended(ScriptSuspendedMessage message)
    {
        var frames = _callstacks.ParseStack(message.Callstack, _dropDuplicateOutermost);
        StoppedEventReason reason;
        long sequence;
        lock (_gate)
        {
            _frames = frames;
            _handles.Clear();
            _nextHandle = 1;
            sequence = ++_stopSequence;
            reason = HitsBreakpoint(frames)
                ? StoppedEventReason.Breakpoint
                : _pendingReason ?? StoppedEventReason.Pause;
            _pendingReason = null;
        }

        var top = frames.Count > 0 ? frames[0] : null;
        _logger.LogInformation("Script {Script} stopped at {Source}:{Line} (stop {Sequence})",
            message.ScriptId, top?.Source, top?.Line, sequence);
        _server?.SendStopped(new StoppedEvent
        {
            Reason = reason,
            ThreadId = ThreadIdFor(message.ScriptId),
            AllThreadsStopped = true,
            Description = top is null ? null : $"{FileName(top.Source)}:{top.Line}"
        });
        NotifyScriptsChanged(LuaScriptsChangedReasons.Suspended, message.ScriptId);
    }

    private void OnScriptAdded(ScriptEntry script)
    {
        // Remember the game's spelling, as for the initial list, so a breakpoint in this file goes out in it.
        _map.ResolveToDocumentUri(script.FullPathName);
        NotifyScriptsChanged(LuaScriptsChangedReasons.ScriptAdded, script.ScriptId);
    }

    private void OnScriptRemoved(int scriptId)
    {
        NotifyScriptsChanged(LuaScriptsChangedReasons.ScriptRemoved, scriptId);
    }

    private void OnOutput(OutputMessage message)
    {
        _server?.SendOutput(new OutputEvent
        {
            Category = message.OutputType == 1 ? OutputEventCategory.StandardOutput : OutputEventCategory.Console,
            Output = message.Text.EndsWith('\n') ? message.Text : message.Text + "\n"
        });
    }

    private void OnConnectionLost(Exception reason)
    {
        _server?.SendOutput(new OutputEvent
        {
            Category = OutputEventCategory.StandardError,
            Output = reason.Message + "\n"
        });
        _server?.SendTerminated(new TerminatedEvent());
    }

    // -- internals ----------------------------------------------------------------------------

    private ILuaDebugSession PrepareSession(ILuaSessionSettings settings)
    {
        _map = _maps.Create(settings.SourceRoots ?? []);
        _unsafeTableExpansion = settings.UnsafeTableExpansion;
        _dropDuplicateOutermost = settings.DropDuplicateOutermostFrame;
        return _sessions.Create();
    }

    private async Task ConnectedAsync(ILuaDebugSession session, CancellationToken cancellationToken)
    {
        _session = session;
        session.Suspended += OnSuspended;
        session.ScriptAdded += OnScriptAdded;
        session.ScriptRemoved += OnScriptRemoved;
        session.Output += OnOutput;
        session.ConnectionLost += OnConnectionLost;

        // Remember how the game spells each file, so breakpoints go out in that spelling.
        foreach (var script in session.Scripts.Values)
            _map.ResolveToDocumentUri(script.FullPathName);

        // The initialized event itself is the protocol library's: it goes out right after the
        // initialize response, which is why breakpoints can already be waiting here.
        await ApplyPendingBreakpointsAsync(session, cancellationToken).ConfigureAwait(false);
    }

    private async Task EndSessionAsync(bool terminateDebuggee, CancellationToken cancellationToken)
    {
        var session = _session;
        _session = null;
        if (session is not null)
        {
            session.Suspended -= OnSuspended;
            session.ScriptAdded -= OnScriptAdded;
            session.ScriptRemoved -= OnScriptRemoved;
            session.Output -= OnOutput;
            session.ConnectionLost -= OnConnectionLost;
            try
            {
                await session.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogWarning("Disconnecting from the game failed: {Reason}", e.Message);
            }

            await session.DisposeAsync().ConfigureAwait(false);
        }

        if (_process is not null && terminateDebuggee)
        {
            _logger.LogInformation("Terminating the launched game process {Id}", _process.Id);
            _process.Kill();
        }
    }

    private static IPEndPoint EndPoint(ILuaSessionSettings settings)
    {
        if (!IPAddress.TryParse(settings.Host, out var address))
        {
            var resolved = Dns.GetHostAddresses(settings.Host)
                               .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                           ?? throw new LuaDebugAdapterException(
                               $"Cannot resolve host '{settings.Host}' to an IPv4 address");
            address = resolved;
        }

        return new IPEndPoint(address, settings.Port);
    }

    private static string ClientName(ILuaSessionSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.ClientName)
            ? $"AetLuaDebugger:{Environment.ProcessId}"
            : settings.ClientName;
    }

    private ILuaDebugSession RequireSession()
    {
        return _session ?? throw new LuaDebugAdapterException("Not attached to the game");
    }

    private CallstackFrame FrameFor(long frameId)
    {
        lock (_gate)
        {
            if (frameId / FramesPerStop != _stopSequence)
                throw new LuaDebugAdapterException("That frame belongs to an earlier stop");
            var level = (int)(frameId % FramesPerStop);
            return _frames.FirstOrDefault(f => f.Level == level)
                   ?? throw new LuaDebugAdapterException($"No frame with level {level}");
        }
    }

    private void ClearStop(StoppedEventReason? pending = null)
    {
        lock (_gate)
        {
            _frames = [];
            _handles.Clear();
            _pendingReason = pending;
        }

        NotifyScriptsChanged(LuaScriptsChangedReasons.StateChanged);
    }

    private bool HitsBreakpoint(IReadOnlyList<CallstackFrame> frames)
    {
        if (frames.Count == 0)
            return false;
        var top = frames[0];
        var uri = _map.ResolveToDocumentUri(top.Source);
        return uri is not null && _breakpoints.TryGetValue(uri, out var lines) && lines.ContainsKey(top.Line);
    }

    private long NewHandle(object target)
    {
        lock (_gate)
        {
            var handle = _nextHandle++;
            _handles[handle] = target;
            return handle;
        }
    }

    private long TableReference(int scriptId, string name, uint[] path, int valueType)
    {
        return _unsafeTableExpansion && LuaTypeNames.IsTable(valueType)
            ? NewHandle(new TableHandle(scriptId, name, path))
            : 0;
    }

    private async Task<List<Variable>> LocalsAsync(ILuaDebugSession session, LocalsHandle handle,
        CancellationToken cancellationToken)
    {
        var uri = _map.ResolveToDocumentUri(handle.Frame.Source);
        var path = uri is null ? null : _fileHelper.FileUriToPath(uri);
        if (path is null || !_fileHelper.FileSystem.File.Exists(path))
            return [];

        var text = await _fileHelper.FileSystem.File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var variables = new List<Variable>();
        foreach (var local in _locals.LocalsAt(text, handle.Frame.Line))
        {
            var value = await session.DumpVariableAsync(handle.ScriptId, local.Name, cancellationToken)
                .ConfigureAwait(false);
            variables.Add(new Variable
            {
                Name = local.Name,
                Value = value.ValueText,
                Type = LuaTypeNames.Of(value.ValueType),
                EvaluateName = local.Name,
                VariablesReference = TableReference(handle.ScriptId, local.Name, [], value.ValueType)
            });
        }

        return variables;
    }

    private async Task<List<Variable>> TableAsync(ILuaDebugSession session, TableHandle handle,
        CancellationToken cancellationToken)
    {
        var dump = await session.DumpTableAsync(handle.ScriptId, handle.Name, handle.Path, cancellationToken)
            .ConfigureAwait(false);
        var variables = new List<Variable>(dump.Members.Count);
        for (var index = 0; index < dump.Members.Count; index++)
        {
            var member = dump.Members[index];
            // A nested table descends by member position; the game's descent path is positional.
            var childPath = handle.Path.Append((uint)index).ToArray();
            variables.Add(new Variable
            {
                Name = member.KeyText,
                Value = member.ValueText,
                Type = LuaTypeNames.Of(member.ValueType),
                VariablesReference = TableReference(handle.ScriptId, handle.Name, childPath, member.ValueType)
            });
        }

        return variables;
    }

    private Source SourceFor(string gamePath)
    {
        var uri = _map.ResolveToDocumentUri(gamePath);
        var path = uri is null ? null : _fileHelper.FileUriToPath(uri);
        return path is null
            ? new Source { Name = gamePath, PresentationHint = SourcePresentationHint.Deemphasize }
            : new Source { Name = FileName(path), Path = path };
    }

    private static SetBreakpointsResponse Unverified(IReadOnlyList<SourceBreakpoint> requested, string message)
    {
        return new SetBreakpointsResponse
        {
            Breakpoints = new Container<Breakpoint>(requested.Select(b => new Breakpoint
            {
                Verified = false,
                Line = b.Line,
                Message = message
            }))
        };
    }

    private static string TrimPrompt(string text)
    {
        var trimmed = text;
        if (trimmed.EndsWith("\r\n> ", StringComparison.Ordinal))
            trimmed = trimmed[..^4];
        else if (trimmed.EndsWith("\r\n  > ", StringComparison.Ordinal))
            trimmed = trimmed[..^6];
        return trimmed.Trim('\r', '\n');
    }

    private static string FileName(string path)
    {
        var cut = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return cut < 0 ? path : path[(cut + 1)..];
    }

    /// <summary>Script ids start at zero, so they are shifted past the Game thread's id.</summary>
    private static long ThreadIdFor(int scriptId)
    {
        return scriptId + 2L;
    }

    private static int ScriptIdFor(long threadId)
    {
        return (int)(threadId - 2);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    private sealed class BreakpointEntry(long id, int line)
    {
        public long Id { get; } = id;

        public int Line { get; } = line;

        /// <summary>Whether the game has been sent this breakpoint.</summary>
        public bool Applied { get; set; }
    }

    private sealed record LocalsHandle(int ScriptId, CallstackFrame Frame);

    private sealed record TableHandle(int ScriptId, string Name, uint[] Path);
}