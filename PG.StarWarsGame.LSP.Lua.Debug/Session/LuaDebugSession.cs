// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;
using Stateless;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <inheritdoc />
public sealed class LuaDebugSession : ILuaDebugSession
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GoodbyeTimeout = TimeSpan.FromSeconds(2);

    private readonly ILuaDebugConnection _connection;
    private readonly TimeProvider _time;
    private readonly ILogger<LuaDebugSession> _logger;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly List<Waiter> _waiters = [];
    private readonly Dictionary<int, ScriptEntry> _scripts = [];
    private readonly HashSet<int> _attached = [];
    private readonly Dictionary<int, IReadOnlyList<ThreadEntry>> _threads = [];
    private readonly StateMachine<DebugRunState, Trigger> _machine;

    private DebugRunState _state = DebugRunState.Running;
    private Task _consumer = Task.CompletedTask;
    private uint _nextTableRequestId;
    private Exception? _lost;

    public LuaDebugSession(ILuaDebugConnection connection, TimeProvider time, ILogger<LuaDebugSession> logger)
    {
        _connection = connection;
        _time = time;
        _logger = logger;
        _machine = BuildMachine();
    }

    private enum Trigger
    {
        ArmBreakAll,
        ArmBreakThread,
        SelectScript,
        ArmStep,
        Resume,
        Suspend,
        ContextCleared
    }

    public bool IsConnected => _connection.IsConnected && _lost is null;

    public DebugRunState RunState => _state;

    public ArmedBreakKind ArmedKind { get; private set; }

    public int? ContextScriptId { get; private set; }

    public int? SuspendedScriptId { get; private set; }

    public IReadOnlyList<string> Callstack { get; private set; } = [];

    public int? SelectedFrame { get; private set; }

    public IReadOnlyDictionary<int, ScriptEntry> Scripts
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<int, ScriptEntry>(_scripts);
            }
        }
    }

    public IReadOnlyCollection<int> AttachedScriptIds
    {
        get
        {
            lock (_gate)
            {
                return _attached.ToArray();
            }
        }
    }

    public IReadOnlyDictionary<int, IReadOnlyList<ThreadEntry>> ThreadsByScript
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<int, IReadOnlyList<ThreadEntry>>(_threads);
            }
        }
    }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public event Action<ScriptSuspendedMessage>? Suspended;

    public event Action<ScriptEntry>? ScriptAdded;

    public event Action<int>? ScriptRemoved;

    public event Action<OutputMessage>? Output;

    public event Action<Exception>? ConnectionLost;

    // -- lifecycle ----------------------------------------------------------------------------

    public async Task ConnectAsync(IPEndPoint remote, string clientName, CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync(remote, clientName, ConnectTimeout, cancellationToken).ConfigureAwait(false);
        _consumer = Task.Run(ConsumeAsync, CancellationToken.None);
        await RequestScriptsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _connection.DisconnectAsync(GoodbyeTimeout, cancellationToken).ConfigureAwait(false);
        await _consumer.ConfigureAwait(false);
        lock (_gate)
        {
            ResetState();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // -- queries ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ScriptEntry>> RequestScriptsAsync(CancellationToken cancellationToken)
    {
        var reply = await RequestAsync<ScriptListMessage>(new RequestScriptListMessage(), _ => true, cancellationToken)
            .ConfigureAwait(false);

        lock (_gate)
        {
            _scripts.Clear();
            foreach (var script in reply.Scripts)
                _scripts[script.ScriptId] = script;
            _attached.IntersectWith(_scripts.Keys);
            foreach (var stale in _threads.Keys.Where(id => !_scripts.ContainsKey(id)).ToList())
                _threads.Remove(stale);
            if (ContextScriptId is { } context && !_scripts.ContainsKey(context))
                ClearExecution();
        }

        return reply.Scripts;
    }

    public async Task<IReadOnlyList<ThreadEntry>> RequestThreadsAsync(int scriptId, CancellationToken cancellationToken)
    {
        RequireKnownScript(scriptId);
        var reply = await RequestAsync<ThreadListMessage>(
            new RequestThreadListMessage(scriptId), m => m.ScriptId == scriptId, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _threads[scriptId] = reply.Threads;
        }

        return reply.Threads;
    }

    public async Task<IReadOnlyList<string>> AttachAsync(int scriptId, CancellationToken cancellationToken)
    {
        RequireKnownScript(scriptId);
        var reply = await RequestAsync<ChildScriptListMessage>(
            new AttachScriptMessage(scriptId), m => m.ParentScriptId == scriptId, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _attached.Add(scriptId);
        }

        return reply.ChildScriptNames;
    }

    public async Task<VariableDumpMessage> DumpVariableAsync(int scriptId, string variableName,
        CancellationToken cancellationToken)
    {
        RequireKnownScript(scriptId);
        return await RequestAsync<VariableDumpMessage>(
            new DumpVariableMessage(scriptId, variableName),
            m => m.ScriptId == scriptId && m.VariableName == variableName,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TableDumpMessage> DumpTableAsync(int scriptId, string tableName, IReadOnlyList<uint> path,
        CancellationToken cancellationToken)
    {
        RequireKnownScript(scriptId);
        uint requestId;
        lock (_gate)
        {
            requestId = ++_nextTableRequestId;
        }

        return await RequestAsync<TableDumpMessage>(
            new DumpTableMessage(scriptId, requestId, tableName, path),
            m => m.RequestId == requestId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExecuteTextAsync(int scriptId, string text, CancellationToken cancellationToken)
    {
        // The game answers nothing at all for a script it does not have, so this is checked here
        // rather than left to the timeout.
        RequireKnownScript(scriptId);
        var reply = await RequestAsync<ExecuteTextResponseMessage>(
            new ExecuteTextMessage(scriptId, text), m => m.ScriptId == scriptId, cancellationToken).ConfigureAwait(false);
        return reply.ResultText;
    }

    // -- breakpoints --------------------------------------------------------------------------

    public async Task AddBreakpointAsync(int scriptId, int threadId, string sourceName, int line,
        CancellationToken cancellationToken)
    {
        ValidateBreakpointTarget(scriptId, threadId);
        if (scriptId >= 0 && !AttachedScriptIds.Contains(scriptId))
            await AttachAsync(scriptId, cancellationToken).ConfigureAwait(false);
        await _connection.SendAsync(new AddBreakpointMessage(scriptId, threadId, sourceName, line, ""), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task RemoveBreakpointAsync(int scriptId, int threadId, string sourceName, int line,
        CancellationToken cancellationToken)
    {
        ValidateBreakpointTarget(scriptId, threadId);
        return _connection.SendAsync(new RemoveBreakpointMessage(scriptId, threadId, sourceName, line), cancellationToken);
    }

    // -- execution control --------------------------------------------------------------------

    public Task BreakAllAsync(CancellationToken cancellationToken)
    {
        return CommandAsync(Trigger.ArmBreakAll, new BreakAllMessage(), () => ArmedKind = ArmedBreakKind.BreakAll,
            cancellationToken);
    }

    public async Task SelectScriptAsync(int scriptId, CancellationToken cancellationToken)
    {
        RequireKnownScript(scriptId);
        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (ContextScriptId == scriptId)
                    return;
                Require(Trigger.SelectScript);
            }

            await _connection.SendAsync(new SelectScriptMessage(scriptId), cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                ContextScriptId = scriptId;
                SuspendedScriptId = null;
                Callstack = [];
                SelectedFrame = null;
                _machine.Fire(Trigger.SelectScript);
                ArmedKind = ArmedBreakKind.BreakAll;
            }
        }
        finally
        {
            _commands.Release();
        }
    }

    public Task BreakThreadAsync(int threadId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (ContextScriptId is not { } context)
                throw new LuaDebugStateException("Cannot break a thread: no script is selected");
            if (threadId != -1 && !(_threads.TryGetValue(context, out var threads) && threads.Any(t => t.ThreadIndex == threadId)))
                throw new LuaDebugStateException($"Thread {threadId} is not a known thread of script {context}; request its threads first");
        }

        return CommandAsync(Trigger.ArmBreakThread, new BreakThreadMessage(threadId),
            () => ArmedKind = ArmedBreakKind.BreakThread, cancellationToken);
    }

    public Task ContinueAsync(CancellationToken cancellationToken)
    {
        return CommandAsync(Trigger.Resume, new ContinueMessage(), () =>
        {
            ArmedKind = ArmedBreakKind.None;
            SuspendedScriptId = null;
            Callstack = [];
            SelectedFrame = null;
        }, cancellationToken);
    }

    public Task StepOverAsync(CancellationToken cancellationToken)
    {
        return StepAsync(new StepOverMessage(), cancellationToken);
    }

    public Task StepIntoAsync(CancellationToken cancellationToken)
    {
        return StepAsync(new StepIntoMessage(), cancellationToken);
    }

    public Task StepOutAsync(CancellationToken cancellationToken)
    {
        return StepAsync(new StepOutMessage(), cancellationToken);
    }

    public async Task SelectFrameAsync(int level, CancellationToken cancellationToken)
    {
        int scriptId;
        lock (_gate)
        {
            if (_state != DebugRunState.Suspended || SuspendedScriptId is not { } suspended)
                throw new LuaDebugStateException("Cannot select a frame: no script is suspended");
            if (level < 0 || level >= Callstack.Count)
                throw new LuaDebugStateException($"Frame {level} is outside the call stack of {Callstack.Count} frames");
            scriptId = suspended;
        }

        await _connection.SendAsync(new SetCallstackDepthMessage(scriptId, level), cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            SelectedFrame = level;
        }
    }

    // -- internals ----------------------------------------------------------------------------

    private Task StepAsync(LuaDebugMessage step, CancellationToken cancellationToken)
    {
        return CommandAsync(Trigger.ArmStep, step, () =>
        {
            ArmedKind = ArmedBreakKind.Step;
            SuspendedScriptId = null;
            Callstack = [];
            SelectedFrame = null;
        }, cancellationToken);
    }

    /// <summary>Checks the trigger, sends, then fires it: the mirror only moves once the wire has the command.</summary>
    private async Task CommandAsync(Trigger trigger, LuaDebugMessage message, Action afterFire,
        CancellationToken cancellationToken)
    {
        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                Require(trigger);
            }

            await _connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _machine.Fire(trigger);
                afterFire();
            }
        }
        finally
        {
            _commands.Release();
        }
    }

    private void Require(Trigger trigger)
    {
        if (_lost is not null)
            throw new LuaDebugConnectionLostException("The debugger connection is gone", _lost);
        if (!_machine.CanFire(trigger))
            throw new LuaDebugStateException(Refusal(trigger));
    }

    private string Refusal(Trigger trigger)
    {
        return (trigger, _state) switch
        {
            (Trigger.ArmBreakAll, DebugRunState.Suspended) => "Cannot break: a script is already suspended; continue or step first",
            (Trigger.ArmBreakAll, DebugRunState.BreakArmed) => "A break is already armed and waiting for the next Lua line",
            (Trigger.ArmBreakThread, DebugRunState.Suspended) => "Cannot break a thread: a script is already suspended; continue first",
            (Trigger.SelectScript, DebugRunState.Suspended) => "Cannot select a script while one is suspended; continue first",
            (Trigger.ArmStep, _) => "Cannot step: no script is suspended",
            (Trigger.Resume, _) => "Cannot continue: no script is suspended",
            _ => $"Cannot {trigger} while the debugger is {_state}"
        };
    }

    private void RequireKnownScript(int scriptId)
    {
        lock (_gate)
        {
            if (_lost is not null)
                throw new LuaDebugConnectionLostException("The debugger connection is gone", _lost);
            if (!_scripts.ContainsKey(scriptId))
                throw new LuaDebugStateException($"Script {scriptId} is not in the game's script list; refresh it first");
        }
    }

    private void ValidateBreakpointTarget(int scriptId, int threadId)
    {
        if (scriptId == -1 && threadId == -1)
            return;
        if (scriptId < 0)
            throw new LuaDebugStateException("A source-wide breakpoint needs thread id -1 as well as script id -1");
        RequireKnownScript(scriptId);
        lock (_gate)
        {
            if (threadId != -1 && !(_threads.TryGetValue(scriptId, out var threads) && threads.Any(t => t.ThreadIndex == threadId)))
                throw new LuaDebugStateException($"Thread {threadId} is not a known thread of script {scriptId}; request its threads first");
        }
    }

    private async Task<T> RequestAsync<T>(LuaDebugMessage request, Func<T, bool> matches,
        CancellationToken cancellationToken) where T : LuaDebugMessage
    {
        var waiter = new Waiter(m => m is T reply && matches(reply));
        lock (_gate)
        {
            if (_lost is not null)
                throw new LuaDebugConnectionLostException("The debugger connection is gone", _lost);
            _waiters.Add(waiter);
        }

        try
        {
            await _connection.SendAsync(request, cancellationToken).ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(RequestTimeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            try
            {
                return (T)await waiter.Reply.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The game did not answer {request.Id} within {RequestTimeout.TotalSeconds:0.#} s");
            }
        }
        finally
        {
            lock (_gate)
            {
                _waiters.Remove(waiter);
            }
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var message in _connection.Inbound.ReadAllAsync().ConfigureAwait(false))
                Dispatch(message);
        }
        catch (Exception e)
        {
            List<Waiter> pending;
            lock (_gate)
            {
                _lost = e;
                pending = _waiters.ToList();
                _waiters.Clear();
            }

            foreach (var waiter in pending)
                waiter.Reply.TrySetException(e);
            ConnectionLost?.Invoke(e);
        }
    }

    private void Dispatch(LuaDebugMessage message)
    {
        Waiter? waiter;
        lock (_gate)
        {
            waiter = _waiters.FirstOrDefault(w => w.Matches(message));
            if (waiter is not null)
                _waiters.Remove(waiter);
            else
                Track(message);
        }

        if (waiter is not null)
        {
            waiter.Reply.TrySetResult(message);
            return;
        }

        switch (message)
        {
            case ScriptSuspendedMessage suspended:
                Suspended?.Invoke(suspended);
                break;
            case ScriptAddedMessage added:
                ScriptAdded?.Invoke(new ScriptEntry(added.ScriptId, added.FullPathName));
                break;
            case ScriptRemovedMessage removed:
                ScriptRemoved?.Invoke(removed.ScriptId);
                break;
            case OutputMessage output:
                Output?.Invoke(output);
                break;
            case HeartbeatMessage or HelloMessage:
                break;
            default:
                _logger.LogDebug("Unsolicited {Id} from the game was not matched by any request", message.Id);
                break;
        }
    }

    /// <summary>Applies an unsolicited message to the mirror. Called under the gate.</summary>
    private void Track(LuaDebugMessage message)
    {
        switch (message)
        {
            case ScriptSuspendedMessage suspended:
                _scripts.TryAdd(suspended.ScriptId, new ScriptEntry(suspended.ScriptId, suspended.FullPathName));
                _attached.Add(suspended.ScriptId);
                _threads[suspended.ScriptId] = suspended.Threads;
                _machine.Fire(Trigger.Suspend);
                ArmedKind = ArmedBreakKind.None;
                ContextScriptId = suspended.ScriptId;
                SuspendedScriptId = suspended.ScriptId;
                Callstack = suspended.Callstack;
                SelectedFrame = suspended.Callstack.Count == 0 ? null : suspended.Callstack.Count - 1;
                break;

            case ScriptAddedMessage added:
                _scripts[added.ScriptId] = new ScriptEntry(added.ScriptId, added.FullPathName);
                break;

            case ScriptRemovedMessage removed:
                _scripts.Remove(removed.ScriptId);
                _attached.Remove(removed.ScriptId);
                _threads.Remove(removed.ScriptId);
                if (ContextScriptId == removed.ScriptId)
                    ClearExecution();
                break;
        }
    }

    /// <summary>The context script is gone or replaced: nothing is armed or suspended any more. Called under the gate.</summary>
    private void ClearExecution()
    {
        ContextScriptId = null;
        SuspendedScriptId = null;
        Callstack = [];
        SelectedFrame = null;
        ArmedKind = ArmedBreakKind.None;
        if (_machine.CanFire(Trigger.ContextCleared))
            _machine.Fire(Trigger.ContextCleared);
    }

    private void ResetState()
    {
        ClearExecution();
        _scripts.Clear();
        _attached.Clear();
        _threads.Clear();
    }

    private StateMachine<DebugRunState, Trigger> BuildMachine()
    {
        var machine = new StateMachine<DebugRunState, Trigger>(() => _state, s => _state = s);

        machine.Configure(DebugRunState.Running)
            .Permit(Trigger.ArmBreakAll, DebugRunState.BreakArmed)
            .Permit(Trigger.ArmBreakThread, DebugRunState.BreakArmed)
            .Permit(Trigger.SelectScript, DebugRunState.BreakArmed)
            .Permit(Trigger.Suspend, DebugRunState.Suspended)
            .Ignore(Trigger.ContextCleared);

        machine.Configure(DebugRunState.BreakArmed)
            // The game rejects a second break-all while one is armed, but accepts one on top of a
            // step or a thread break.
            .PermitReentryIf(Trigger.ArmBreakAll, () => ArmedKind != ArmedBreakKind.BreakAll)
            .PermitReentry(Trigger.ArmBreakThread)
            .PermitReentry(Trigger.SelectScript)
            .Permit(Trigger.Suspend, DebugRunState.Suspended)
            .Permit(Trigger.ContextCleared, DebugRunState.Running);

        machine.Configure(DebugRunState.Suspended)
            .Permit(Trigger.Resume, DebugRunState.Running)
            .Permit(Trigger.ArmStep, DebugRunState.BreakArmed)
            .PermitReentry(Trigger.Suspend)
            .Permit(Trigger.ContextCleared, DebugRunState.Running);

        machine.OnUnhandledTrigger((state, trigger) =>
            throw new LuaDebugStateException($"Cannot {trigger} while the debugger is {state}"));
        return machine;
    }

    private sealed class Waiter(Func<LuaDebugMessage, bool> matches)
    {
        public Func<LuaDebugMessage, bool> Matches { get; } = matches;

        public TaskCompletionSource<LuaDebugMessage> Reply { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
