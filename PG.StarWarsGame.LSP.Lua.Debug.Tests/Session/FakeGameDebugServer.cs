// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Lua.Debug.PgNet;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;
using PG.StarWarsGame.LSP.Lua.Debug.Session;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.Session;

/// <summary>
///     A stand-in for the game's Lua debug server on a real loopback UDP socket. It speaks the full
///     wire protocol through the production codecs and its own reliable channel, answers requests
///     from configured data, records every decoded request, and records a <see cref="Violations" />
///     entry for every command the game would assert on - so a session that sends one fails a test
///     instead of a game.
/// </summary>
internal sealed class FakeGameDebugServer : IAsyncDisposable
{
    private readonly UdpClient _socket;
    private readonly IPgNetDatagramCodec _datagrams;
    private readonly ILuaMessageCodec _messages;
    private readonly IReliableChannel _channel;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly List<LuaDebugMessage> _received = [];
    private readonly List<string> _violations = [];
    private readonly List<AddBreakpointMessage> _breakpoints = [];
    private readonly HashSet<int> _attached = [];
    private readonly Task _loop;
    private IPEndPoint? _client;

    public FakeGameDebugServer()
    {
        var provider = TestServices.Build();
        _datagrams = provider.GetRequiredService<IPgNetDatagramCodec>();
        _messages = provider.GetRequiredService<ILuaMessageCodec>();
        _channel = provider.GetRequiredService<IReliableChannel>();
        _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        EndPoint = (IPEndPoint)_socket.Client.LocalEndPoint!;
        _loop = Task.Run(LoopAsync);
    }

    public IPEndPoint EndPoint { get; }

    public string ServerName { get; set; } = "StarWarsI:7884";

    public string? ClientName { get; private set; }

    public bool HelloReceived { get; private set; }

    public bool GoodbyeReceived { get; private set; }

    // -- configured world ---------------------------------------------------------------------

    public List<ScriptEntry> Scripts { get; } = [];

    public Dictionary<int, List<ThreadEntry>> Threads { get; } = [];

    public Dictionary<int, List<string>> ChildScripts { get; } = [];

    public Dictionary<(int ScriptId, string Name), (int Type, string Text)> Variables { get; } = [];

    public Dictionary<(int ScriptId, string Table), List<TableMember>> Tables { get; } = [];

    public Func<int, string, string> ExecuteResult { get; set; } = (_, text) => $"ran: {text}";

    /// <summary>When set, thread-list requests are swallowed, so a request times out.</summary>
    public bool IgnoreThreadRequests { get; set; }

    /// <summary>Drop this many inbound guaranteed datagrams before processing them, to force resends.</summary>
    public int DropNextGuaranteed { get; set; }

    // -- mirrored engine state ----------------------------------------------------------------

    public bool IsSuspended { get; private set; }

    public ArmedBreakKind Armed { get; private set; }

    public int? ContextScriptId { get; private set; }

    public int CallstackSize { get; private set; }

    public int SelectedLevel { get; private set; } = -1;

    public IReadOnlyList<LuaDebugMessage> Received
    {
        get
        {
            lock (_gate)
            {
                return _received.ToArray();
            }
        }
    }

    public IReadOnlyList<string> Violations
    {
        get
        {
            lock (_gate)
            {
                return _violations.ToArray();
            }
        }
    }

    public IReadOnlyList<AddBreakpointMessage> Breakpoints
    {
        get
        {
            lock (_gate)
            {
                return _breakpoints.ToArray();
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

    // -- pushes from the game side ------------------------------------------------------------

    public Task SuspendAsync(int scriptId, int threadId, IReadOnlyList<string> callstack,
        IReadOnlyList<ThreadEntry> threads)
    {
        var script = Scripts.First(s => s.ScriptId == scriptId);
        lock (_gate)
        {
            IsSuspended = true;
            Armed = ArmedBreakKind.None;
            ContextScriptId = scriptId;
            CallstackSize = callstack.Count;
            SelectedLevel = callstack.Count - 1;
        }

        return SendAsync(new ScriptSuspendedMessage(scriptId, threadId, script.FullPathName, callstack, threads.Count,
            threads));
    }

    public Task AddScriptAsync(ScriptEntry script)
    {
        Scripts.Add(script);
        return SendAsync(new ScriptAddedMessage(script.ScriptId, script.FullPathName));
    }

    public Task RemoveScriptAsync(int scriptId)
    {
        Scripts.RemoveAll(s => s.ScriptId == scriptId);
        lock (_gate)
        {
            if (ContextScriptId == scriptId)
            {
                ContextScriptId = null;
                IsSuspended = false;
                Armed = ArmedBreakKind.None;
            }
        }

        return SendAsync(new ScriptRemovedMessage(scriptId));
    }

    public Task OutputAsync(int type, string text)
    {
        return SendAsync(new OutputMessage(type, text));
    }

    public Task HeartbeatAsync()
    {
        return SendAsync(new HeartbeatMessage());
    }

    public Task GoodbyeAsync()
    {
        return SendAsync(new GoodbyeMessage());
    }

    public async Task SendAsync(LuaDebugMessage message)
    {
        var client = _client ?? throw new InvalidOperationException("No client has connected yet");
        IReadOnlyList<byte[]> datagrams;
        lock (_gate)
        {
            datagrams = _channel.MakeGuaranteed(_messages.Encode(message));
        }

        foreach (var datagram in datagrams)
            await _socket.SendAsync(datagram, client);
    }

    /// <summary>Waits, in real time, until a request of the given kind has been received.</summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool>? predicate = null, int timeoutMilliseconds = 5000)
        where T : LuaDebugMessage
    {
        using var deadline = new CancellationTokenSource(timeoutMilliseconds);
        while (true)
        {
            lock (_gate)
            {
                var found = _received.OfType<T>().FirstOrDefault(m => predicate is null || predicate(m));
                if (found is not null)
                    return found;
            }

            await Task.Delay(10, deadline.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _socket.Dispose();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
    }

    // -- the wire -----------------------------------------------------------------------------

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _socket.ReceiveAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            var packet = _datagrams.Decode(received.Buffer);
            if (packet.Kind == PgNetPacketKind.NonGuaranteed)
            {
                await AcceptAsync(received.Buffer, received.RemoteEndPoint);
                continue;
            }

            if (!received.RemoteEndPoint.Equals(_client))
                continue;

            if (packet.Kind == PgNetPacketKind.Guaranteed && DropNextGuaranteed > 0)
            {
                DropNextGuaranteed--;
                continue;
            }

            ReliableReceiveResult result;
            lock (_gate)
            {
                result = _channel.Process(packet);
            }

            foreach (var outbound in result.Outbound)
                await _socket.SendAsync(outbound, _client);
            foreach (var payload in result.Deliveries)
                await HandleAsync(_messages.Decode(payload));
        }
    }

    private async Task AcceptAsync(byte[] request, IPEndPoint from)
    {
        var packet = _datagrams.Decode(request);
        var reader = packet.Payload.CreateReader();
        if (reader.ReadUInt32() != ConnectHandshake.Magic || reader.ReadString() != ConnectHandshake.ClientGreeting)
            return;
        ClientName = reader.ReadString();
        _client = from;

        var reply = new BitWriter();
        reply.WriteUInt32(ConnectHandshake.Magic);
        reply.WriteString(ConnectHandshake.ServerGreeting);
        reply.WriteString(ServerName);
        IReadOnlyList<byte[]> datagrams;
        lock (_gate)
        {
            datagrams = _channel.MakeGuaranteed(reply.ToBuffer());
        }

        foreach (var datagram in datagrams)
            await _socket.SendAsync(datagram, from);
    }

    private async Task HandleAsync(LuaDebugMessage message)
    {
        lock (_gate)
        {
            _received.Add(message);
        }

        switch (message)
        {
            case HelloMessage:
                HelloReceived = true;
                await SendAsync(new HelloMessage());
                break;

            case GoodbyeMessage:
                GoodbyeReceived = true;
                lock (_gate)
                {
                    _breakpoints.Clear();
                    _attached.Clear();
                }

                break;

            case RequestScriptListMessage:
                await SendAsync(new ScriptListMessage(Scripts.ToList()));
                break;

            case RequestThreadListMessage m:
                if (IgnoreThreadRequests)
                    break;
                var threads = Threads.GetValueOrDefault(m.ScriptId, []);
                await SendAsync(new ThreadListMessage(m.ScriptId, threads.Count, threads));
                break;

            case AttachScriptMessage m:
                lock (_gate)
                {
                    if (Scripts.Any(s => s.ScriptId == m.ScriptId))
                        _attached.Add(m.ScriptId);
                }

                await SendAsync(new ChildScriptListMessage(m.ScriptId, ChildScripts.GetValueOrDefault(m.ScriptId, [])));
                break;

            case AddBreakpointMessage m:
                lock (_gate)
                {
                    var known = Scripts.Any(s => s.ScriptId == m.ScriptId);
                    if (m.ThreadId != -1 && !known)
                        Violate($"breakpoint with thread {m.ThreadId} on unknown script {m.ScriptId}");
                    else if (m.ThreadId != -1 &&
                             !Threads.GetValueOrDefault(m.ScriptId, []).Any(t => t.ThreadIndex == m.ThreadId))
                        Violate($"breakpoint on thread {m.ThreadId} that script {m.ScriptId} does not have");
                    else
                        _breakpoints.Add(m);
                }

                break;

            case RemoveBreakpointMessage m:
                lock (_gate)
                {
                    _breakpoints.RemoveAll(b => b.ScriptId == m.ScriptId && b.ThreadId == m.ThreadId
                                                                         && b.SourceName == m.SourceName &&
                                                                         b.Line == m.Line);
                }

                break;

            case BreakAllMessage:
                lock (_gate)
                {
                    if (IsSuspended)
                        Violate("break-all while suspended");
                    else if (Armed == ArmedBreakKind.BreakAll)
                        Violate("break-all while a break-all is already armed");
                    else
                        Armed = ArmedBreakKind.BreakAll;
                }

                break;

            case SelectScriptMessage m:
                lock (_gate)
                {
                    if (Scripts.Any(s => s.ScriptId == m.ScriptId) && ContextScriptId != m.ScriptId)
                    {
                        IsSuspended = false;
                        ContextScriptId = m.ScriptId;
                        Armed = ArmedBreakKind.BreakAll;
                    }
                }

                break;

            case BreakThreadMessage m:
                lock (_gate)
                {
                    if (ContextScriptId is not { } context)
                        Violate("break-thread with no context script");
                    else if (m.ThreadId != -1 &&
                             !Threads.GetValueOrDefault(context, []).Any(t => t.ThreadIndex == m.ThreadId))
                        Violate($"break-thread on thread {m.ThreadId} that script {context} does not have");
                    else
                    {
                        Armed = ArmedBreakKind.BreakThread;
                        IsSuspended = false;
                    }
                }

                break;

            case ContinueMessage:
                lock (_gate)
                {
                    if (!IsSuspended)
                        Violate("continue while not suspended");
                    IsSuspended = false;
                    Armed = ArmedBreakKind.None;
                }

                break;

            case StepOverMessage or StepIntoMessage or StepOutMessage:
                lock (_gate)
                {
                    if (!IsSuspended)
                        Violate($"{message.Id} while not suspended");
                    IsSuspended = false;
                    Armed = ArmedBreakKind.Step;
                }

                break;

            case SetCallstackDepthMessage m:
                lock (_gate)
                {
                    if (!IsSuspended)
                        Violate("set-callstack-depth while not suspended");
                    else if (m.Level < 0 || m.Level >= CallstackSize)
                        Violate($"set-callstack-depth level {m.Level} outside {CallstackSize} frames");
                    else
                        SelectedLevel = m.Level;
                }

                break;

            case DumpVariableMessage m:
                var known2 = Scripts.Any(s => s.ScriptId == m.ScriptId);
                var (type, text) = known2
                    ? Variables.GetValueOrDefault((m.ScriptId, m.VariableName), (0, "nil"))
                    : (-1, "");
                await SendAsync(new VariableDumpMessage(m.ScriptId, m.VariableName, type, text));
                break;

            case DumpTableMessage m:
                await SendAsync(new TableDumpMessage(m.RequestId,
                    Tables.GetValueOrDefault((m.ScriptId, m.TableName), [])));
                break;

            case ExecuteTextMessage m:
                // The game logs and sends nothing for a script it does not have.
                if (Scripts.Any(s => s.ScriptId == m.ScriptId))
                    await SendAsync(new ExecuteTextResponseMessage(m.ScriptId,
                        ExecuteResult(m.ScriptId, m.Text) + "\r\n> "));
                break;
        }
    }

    private void Violate(string what)
    {
        _violations.Add(what);
    }
}