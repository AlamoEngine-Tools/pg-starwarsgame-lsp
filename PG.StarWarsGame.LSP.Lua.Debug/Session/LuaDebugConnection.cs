// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Lua.Debug.PgNet;
using PG.StarWarsGame.LSP.Lua.Debug.Protocol;

namespace PG.StarWarsGame.LSP.Lua.Debug.Session;

/// <inheritdoc />
public sealed class LuaDebugConnection : ILuaDebugConnection
{
    /// <summary>How often pending resends and the silence watchdog are checked.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    private readonly IUdpTransportFactory _transports;
    private readonly IPgNetDatagramCodec _datagrams;
    private readonly IConnectHandshake _handshake;
    private readonly ILuaMessageCodec _messages;
    private readonly IReliableChannel _channel;
    private readonly TimeProvider _time;
    private readonly ILogger<LuaDebugConnection> _logger;
    private readonly Channel<LuaDebugMessage> _inbound = Channel.CreateUnbounded<LuaDebugMessage>();
    private readonly Lock _gate = new();

    private IUdpTransport? _transport;
    private CancellationTokenSource? _loops;
    private Task _receiveLoop = Task.CompletedTask;
    private Task _tickLoop = Task.CompletedTask;
    private long _lastActivity;
    private TaskCompletionSource _drained = Completed();
    private bool _closed;

    public LuaDebugConnection(
        IUdpTransportFactory transports,
        IPgNetDatagramCodec datagrams,
        IConnectHandshake handshake,
        ILuaMessageCodec messages,
        IReliableChannel channel,
        TimeProvider time,
        ILogger<LuaDebugConnection> logger)
    {
        _transports = transports;
        _datagrams = datagrams;
        _handshake = handshake;
        _messages = messages;
        _channel = channel;
        _time = time;
        _logger = logger;
    }

    public bool IsConnected { get; private set; }

    public string? ServerName { get; private set; }

    public IPEndPoint? LocalEndPoint => _transport?.LocalEndPoint;

    public TimeSpan SilenceTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public ChannelReader<LuaDebugMessage> Inbound => _inbound.Reader;

    public async Task ConnectAsync(IPEndPoint remote, string clientName, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (_transport is not null)
            throw new InvalidOperationException("The connection has already been opened");

        _transport = _transports.Open(remote);
        _lastActivity = _time.GetTimestamp();
        using var deadline = new CancellationTokenSource(timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await _transport.SendAsync(_handshake.BuildRequest(clientName), linked.Token).ConfigureAwait(false);
            ServerName = await AwaitConnectReplyAsync(linked.Token).ConfigureAwait(false);

            _loops = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_loops.Token), CancellationToken.None);
            _tickLoop = Task.Run(() => TickLoopAsync(_loops.Token), CancellationToken.None);

            await SendAsync(new HelloMessage(), linked.Token).ConfigureAwait(false);
            await AwaitHelloAsync(linked.Token).ConfigureAwait(false);
            IsConnected = true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested &&
                                                 !cancellationToken.IsCancellationRequested)
        {
            Fail(new TimeoutException(
                $"The game at {remote} did not complete the debugger handshake within {timeout.TotalSeconds:0.#} s"));
            throw new TimeoutException(
                $"The game at {remote} did not complete the debugger handshake within {timeout.TotalSeconds:0.#} s. " +
                "Is a debug build running with its Lua debug server started (console command luadebug)?");
        }
    }

    public async Task SendAsync(LuaDebugMessage message, CancellationToken cancellationToken)
    {
        var transport = _transport ?? throw new InvalidOperationException("The connection is not open");
        IReadOnlyList<byte[]> datagrams;
        lock (_gate)
        {
            datagrams = _channel.MakeGuaranteed(_messages.Encode(message));
            if (_drained.Task.IsCompleted)
                _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        foreach (var datagram in datagrams)
            await transport.SendAsync(datagram, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_transport is null || _closed)
            return;

        if (IsConnected)
        {
            try
            {
                await SendAsync(new GoodbyeMessage(), cancellationToken).ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(timeout, _time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                await _drained.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("The game did not acknowledge goodbye within {Timeout}", timeout);
            }
            catch (LuaDebugConnectionLostException)
            {
                // Already gone; nothing left to say goodbye to.
            }
        }

        Close(null);
        // Both loops end on their own once the socket is gone; waiting makes shutdown deterministic.
        await Task.WhenAll(_receiveLoop, _tickLoop).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<string> AwaitConnectReplyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var datagram = await _transport!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            _lastActivity = _time.GetTimestamp();
            ConnectResponse response;
            try
            {
                response = _handshake.ParseResponse(datagram);
            }
            catch (PgNetProtocolException e)
            {
                _logger.LogDebug("Ignoring a datagram that is not the connect reply: {Reason}", e.Message);
                continue;
            }

            // A guaranteed reply is part of the reliable stream and must be acknowledged like any other.
            if (response.Packet.Kind == PgNetPacketKind.Guaranteed)
                await SendOutboundAsync(Process(response.Packet), cancellationToken).ConfigureAwait(false);
            return response.ServerName;
        }
    }

    private async Task AwaitHelloAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await _inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (message is HelloMessage)
                return;
            _logger.LogDebug("Message {Id} arrived before the debugger hello; dropped", message.Id);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var datagram = await _transport!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                _lastActivity = _time.GetTimestamp();

                PgNetPacket packet;
                try
                {
                    packet = _datagrams.Decode(datagram);
                }
                catch (PgNetProtocolException e)
                {
                    _logger.LogWarning("Dropped a malformed datagram of {Length} bytes: {Reason}", datagram.Length,
                        e.Message);
                    continue;
                }

                var result = Process(packet);
                await SendOutboundAsync(result, cancellationToken).ConfigureAwait(false);

                foreach (var payload in result.Deliveries)
                {
                    LuaDebugMessage message;
                    try
                    {
                        message = _messages.Decode(payload);
                    }
                    catch (PgNetProtocolException e)
                    {
                        _logger.LogWarning("Dropped an undecodable debugger message: {Reason}", e.Message);
                        continue;
                    }

                    if (message is GoodbyeMessage)
                    {
                        Fail(new LuaDebugConnectionLostException("The game closed the debugger connection"));
                        return;
                    }

                    _inbound.Writer.TryWrite(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Fail(e is LuaDebugConnectionLostException
                ? e
                : new LuaDebugConnectionLostException("The debugger connection failed", e));
        }
    }

    private async Task TickLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(Tick, _time, cancellationToken).ConfigureAwait(false);

                if (_time.GetElapsedTime(_lastActivity) > SilenceTimeout)
                {
                    Fail(new LuaDebugConnectionLostException(
                        $"No packet from the game for {SilenceTimeout.TotalSeconds:0.#} s; the connection is presumed lost"));
                    return;
                }

                IReadOnlyList<byte[]> due;
                lock (_gate)
                {
                    due = _channel.DueResends();
                }

                foreach (var datagram in due)
                {
                    _logger.LogDebug("Resending an unacknowledged datagram of {Length} bytes", datagram.Length);
                    await _transport!.SendAsync(datagram, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Fail(new LuaDebugConnectionLostException("The debugger connection failed", e));
        }
    }

    private ReliableReceiveResult Process(PgNetPacket packet)
    {
        lock (_gate)
        {
            var result = _channel.Process(packet);
            if (_channel.PendingCount == 0)
                _drained.TrySetResult();
            return result;
        }
    }

    private async Task SendOutboundAsync(ReliableReceiveResult result, CancellationToken cancellationToken)
    {
        foreach (var datagram in result.Outbound)
            await _transport!.SendAsync(datagram, cancellationToken).ConfigureAwait(false);
    }

    private void Fail(Exception reason)
    {
        _logger.LogInformation("Debugger connection ended: {Reason}", reason.Message);
        Close(reason);
    }

    private void Close(Exception? reason)
    {
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            IsConnected = false;
        }

        _loops?.Cancel();
        _transport?.Dispose();
        if (reason is null)
            _inbound.Writer.TryComplete();
        else
            _inbound.Writer.TryComplete(reason);
        lock (_gate)
        {
            _drained.TrySetException(reason ?? new LuaDebugConnectionLostException("The connection was closed"));
        }
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}