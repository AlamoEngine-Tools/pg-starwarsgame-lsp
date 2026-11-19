// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>Hands the preview's large payloads to the client outside the JSON-RPC stream.</summary>
public interface IPreviewTransferChannel
{
    /// <summary>
    ///     Puts a payload up for collection and says where and under which id the client collects it.
    /// </summary>
    /// <param name="format">What the bytes are, for the client's decoder: <c>glb</c>.</param>
    /// <param name="length">The payload's length in bytes.</param>
    /// <param name="open">Opens the payload when the client asks; a stream of exactly that length.</param>
    PreviewTransfer Offer(string format, long length, Func<Stream> open);
}

/// <summary>
///     The frames on the transfer pipe. All integers little-endian.
/// </summary>
/// <remarks>
///     <code>
///     client -> server   16 bytes   the transfer id: the bytes its 32 hex digits spell, in order
///     server -> client    1 byte    status
///                         8 bytes   payload length, zero unless the status is Ok
///                         n bytes   the payload
///     </code>
///     A connection carries any number of these exchanges one after another and is closed by the
///     client. Nothing on the pipe says what a payload is or which asset it belongs to: the JSON-RPC
///     reply that named the id said both.
/// </remarks>
public static class PreviewTransferFraming
{
    public const int RequestLength = 16;
    public const int ResponseHeaderLength = 9;

    /// <summary>The payload follows.</summary>
    public const byte Ok = 0;

    /// <summary>No transfer of that id is waiting: never offered, already collected, or expired.</summary>
    public const byte Unknown = 1;

    /// <summary>The transfer was waiting and its payload could not be opened any more.</summary>
    public const byte Gone = 2;

    public static void WriteResponseHeader(Span<byte> header, byte status, long length)
    {
        header[0] = status;
        BinaryPrimitives.WriteInt64LittleEndian(header[1..], length);
    }

    public static (byte Status, long Length) ReadResponseHeader(ReadOnlySpan<byte> header)
    {
        return (header[0], BinaryPrimitives.ReadInt64LittleEndian(header[1..]));
    }
}

/// <summary>One instance of the transfer pipe: it waits for one client and is then that client's connection.</summary>
public interface IPreviewPipeListener : IAsyncDisposable
{
    /// <summary>The bytes to and from the client, once one has connected.</summary>
    Stream Connection { get; }

    /// <summary>Completes when a client has connected to this instance.</summary>
    Task WaitForConnectionAsync(CancellationToken stop);
}

/// <summary>
///     The transfer channel as a named pipe this server opens for its own client.
/// </summary>
/// <remarks>
///     <para>
///         <strong>A byte channel and nothing else.</strong> Resolving, the cache, the conversion and
///         the choice of clips all happen in the request handler, which then offers the result here.
///         The pipe looks an id up and copies a stream.
///     </para>
///     <para>
///         <strong>Opened on the first offer</strong>, so a session that never opens a preview has no
///         pipe. Restricted to the current user. A transfer is collected once; one nobody collects
///         is dropped <see cref="Unclaimed" /> after it was offered, on a timer of its own, because
///         an offer may hold its payload in memory - a model converted with no project to keep it
///         in, a texture out of an archive.
///     </para>
///     <para>
///         <strong>An offer names a pipe that is listening.</strong> An instance that breaks while
///         it waits is replaced. After <see cref="ListenAttempts" /> broken in a row listening is
///         given up, and the next offer starts it again; an offer made while no instance can be
///         opened fails rather than name a pipe nothing listens on.
///     </para>
///     <para>
///         <strong>A payload is not held open by a client that stopped reading.</strong> One write
///         waits <see cref="WriteStall" /> at most; then the connection is given up and the payload
///         closed - for a loose texture that is the modder's own file.
///     </para>
///     <para>
///         On Windows the client connects to <c>\\.\pipe\&lt;name&gt;</c>. Elsewhere .NET backs a named
///         pipe with a Unix domain socket in the temp directory and the client connects to that path -
///         INFERRED from the runtime's documented behaviour, not run on Linux.
///     </para>
/// </remarks>
public sealed class PreviewTransferPipe(
    ILogger<PreviewTransferPipe> logger,
    TimeProvider? time = null,
    Func<string, IPreviewPipeListener>? listen = null)
    : IPreviewTransferChannel, IDisposable
{
    /// <summary>How long an offer nobody collects is kept.</summary>
    public static readonly TimeSpan Unclaimed = TimeSpan.FromSeconds(30);

    /// <summary>How long one write may wait on a client that has stopped reading.</summary>
    public static readonly TimeSpan WriteStall = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How many instances of the pipe may break in a row before listening is given up until the
    ///     next offer.
    /// </summary>
    public const int ListenAttempts = 5;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Func<string, IPreviewPipeListener> _listen = listen ?? (name => new NamedPipeListener(name));
    private readonly ConcurrentDictionary<Guid, Offered> _offers = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Task _accepting = Task.CompletedTask;
    private bool _acceptingNow;

    /// <summary>The timer that drops unclaimed offers, and whether it is set to fire.</summary>
    private ITimer? _sweep;

    private bool _sweepArmed;

    /// <summary>The instance of the pipe the next client connects to, or null when none stands.</summary>
    private IPreviewPipeListener? _waiting;

    /// <summary>The pipe's bare name.</summary>
    public string Name { get; } = $"aetswg-preview-{Environment.ProcessId}-{Guid.NewGuid():N}";

    /// <summary>
    ///     Completes when nothing is accepting connections: before the first offer, and once
    ///     accepting has ended. For tests.
    /// </summary>
    public Task Accepting
    {
        get
        {
            lock (_gate)
            {
                return _accepting;
            }
        }
    }

    /// <summary>What the client connects to.</summary>
    public string ClientPath => OperatingSystem.IsWindows()
        ? @"\\.\pipe\" + Name
        : Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + Name);

    public PreviewTransfer Offer(string format, long length, Func<Stream> open)
    {
        EnsureListening();
        DropUnclaimed();

        var id = Guid.NewGuid();
        lock (_gate)
        {
            // Added and armed as one step: the sweep decides under the same gate whether anything
            // is left to come back for.
            _offers[id] = new Offered(length, open, _time.GetUtcNow());
            if (!_sweepArmed)
            {
                _sweepArmed = true;
                if (_sweep is null)
                    _sweep = _time.CreateTimer(_ => Sweep(), null, Unclaimed, Timeout.InfiniteTimeSpan);
                else
                    _sweep.Change(Unclaimed, Timeout.InfiniteTimeSpan);
            }
        }

        return new PreviewTransfer(ClientPath, id.ToString("N"), length, format);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        lock (_gate)
        {
            _sweep?.Dispose();
            _sweep = null;
        }

        _offers.Clear();
    }

    /// <summary>
    ///     Has an instance of the pipe standing, and something accepting on it, before an offer
    ///     names the pipe - so a client that connects the moment it has the reply finds it there.
    ///     What opening an instance throws reaches the caller: the offer is not made.
    /// </summary>
    private void EnsureListening()
    {
        lock (_gate)
        {
            _waiting ??= _listen(Name);
            if (_acceptingNow)
                return;

            _acceptingNow = true;
            var stop = _stop.Token;
            _accepting = Task.Run(() => AcceptAsync(stop), stop);
        }
    }

    /// <summary>
    ///     Hands each client that connects to <see cref="ServeAsync" /> and stands the next instance
    ///     up, until the server stops or <see cref="ListenAttempts" /> instances in a row have
    ///     broken. Every way out but the server stopping leaves <c>_acceptingNow</c> false and
    ///     <c>_waiting</c> null under the gate, in one step, so the next offer starts over cleanly.
    /// </summary>
    private async Task AcceptAsync(CancellationToken stop)
    {
        var broken = 0;
        try
        {
            while (true)
            {
                IPreviewPipeListener listener;
                lock (_gate)
                {
                    if (_waiting is null && !TryListen())
                    {
                        _acceptingNow = false;
                        return;
                    }

                    listener = _waiting!;
                }

                try
                {
                    await listener.WaitForConnectionAsync(stop);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // This instance is of no use any more; another takes its place.
                    broken++;
                    lock (_gate)
                    {
                        _waiting = null;
                        if (broken >= ListenAttempts)
                            _acceptingNow = false;
                    }

                    await listener.DisposeAsync();
                    if (broken >= ListenAttempts)
                    {
                        logger.LogWarning(e,
                            "The preview transfer pipe {Pipe} stopped listening after {Attempts} broken instances",
                            Name, broken);
                        return;
                    }

                    logger.LogWarning(e, "An instance of the preview transfer pipe {Pipe} broke; opening another",
                        Name);
                    continue;
                }

                broken = 0;
                lock (_gate)
                {
                    // The next instance stands before this client is served. If it cannot be opened
                    // the top of the loop tries once more and gives up from there.
                    _waiting = null;
                    TryListen();
                }

                _ = Task.Run(() => ServeAsync(listener, stop), stop);
            }
        }
        catch (OperationCanceledException)
        {
            // The server is stopping.
            IPreviewPipeListener? left;
            lock (_gate)
            {
                left = _waiting;
                _waiting = null;
                _acceptingNow = false;
            }

            if (left is not null)
                await left.DisposeAsync();
        }
    }

    /// <summary>Opens an instance of the pipe into <c>_waiting</c>. Called under the gate.</summary>
    private bool TryListen()
    {
        try
        {
            _waiting = _listen(Name);
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "The preview transfer pipe {Pipe} could not open an instance", Name);
            return false;
        }
    }

    private async Task ServeAsync(IPreviewPipeListener client, CancellationToken stop)
    {
        var request = new byte[PreviewTransferFraming.RequestLength];
        var header = new byte[PreviewTransferFraming.ResponseHeaderLength];

        try
        {
            await using (client)
            {
                var connection = client.Connection;
                while (await connection.ReadAtLeastAsync(request, request.Length, false, stop) == request.Length)
                {
                    // Big-endian: the id travels as the bytes its hex digits spell, so a client
                    // needs no knowledge of how a GUID is laid out in memory.
                    var (status, length, payload) = Take(new Guid(request, true));
                    await using (payload)
                    {
                        PreviewTransferFraming.WriteResponseHeader(header, status, length);
                        await WriteAsync(connection, header, stop);

                        if (payload is not null)
                            await CopyExactlyAsync(payload, connection, length, stop);

                        await connection.FlushAsync(stop);
                    }
                }
            }
        }
        catch (StalledWriteException)
        {
            // The client stopped reading. Its connection and the payload are closed by now.
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("A preview transfer was given up: the client read nothing for {Stall}", WriteStall);
        }
        catch (OperationCanceledException)
        {
            // The server is stopping.
        }
        catch (IOException e)
        {
            // The client went away mid-transfer: its panel closed. Nothing to recover.
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug(e, "A preview transfer connection closed early");
        }
    }

    private (byte Status, long Length, Stream? Payload) Take(Guid id)
    {
        if (!_offers.TryRemove(id, out var offered))
            return (PreviewTransferFraming.Unknown, 0, null);

        try
        {
            return (PreviewTransferFraming.Ok, offered.Length, offered.Open());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "A preview transfer's payload could not be opened");
            return (PreviewTransferFraming.Gone, 0, null);
        }
    }

    /// <summary>Drops the offers whose time is up, and comes back for the oldest one left.</summary>
    private void Sweep()
    {
        DropUnclaimed();

        lock (_gate)
        {
            if (_sweep is null)
                return;

            DateTimeOffset? oldest = null;
            foreach (var offered in _offers.Values)
                if (oldest is null || offered.At < oldest)
                    oldest = offered.At;

            if (oldest is null)
            {
                _sweepArmed = false;
                return;
            }

            var due = oldest.Value + Unclaimed - _time.GetUtcNow();
            _sweep.Change(due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    private void DropUnclaimed()
    {
        var before = _time.GetUtcNow() - Unclaimed;
        foreach (var (id, offered) in _offers)
            if (offered.At <= before)
                _offers.TryRemove(id, out _);
    }

    /// <summary>
    ///     Sends exactly the bytes the header announced. A source that ends early - a file that
    ///     shrank between the offer and the collection - cannot fill the frame, so the connection is
    ///     given up: the client sees it close mid-payload, which it reports, instead of reading the
    ///     next frame out of step.
    /// </summary>
    private async Task CopyExactlyAsync(Stream source, Stream target, long length, CancellationToken stop)
    {
        var buffer = new byte[128 * 1024];
        var left = length;
        while (left > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), stop);
            if (read == 0)
            {
                await target.FlushAsync(stop);
                throw new EndOfStreamException("A preview transfer's payload ended before its announced length.");
            }

            await WriteAsync(target, buffer.AsMemory(0, read), stop);
            left -= read;
        }
    }

    /// <summary>
    ///     One write, given up when the client takes none of it for <see cref="WriteStall" />.
    /// </summary>
    /// <remarks>
    ///     The limit is on NO PROGRESS, not on the transfer: every write starts its own, so a large
    ///     payload to a client that reads slowly is sent for as long as it keeps reading. And each
    ///     write has a timer and a token of its own, gone when the write returns - a timer that
    ///     fires just as its write completes has nothing later to cancel, neither the rest of the
    ///     response nor the next exchange on the connection.
    /// </remarks>
    private async ValueTask WriteAsync(Stream target, ReadOnlyMemory<byte> bytes, CancellationToken stop)
    {
        using var stalled = new CancellationTokenSource(WriteStall, _time);
        using var writing = CancellationTokenSource.CreateLinkedTokenSource(stop, stalled.Token);
        try
        {
            await target.WriteAsync(bytes, writing.Token);
        }
        catch (OperationCanceledException) when (stalled.IsCancellationRequested && !stop.IsCancellationRequested)
        {
            throw new StalledWriteException();
        }
    }

    /// <summary>A write the client took nothing of for <see cref="WriteStall" />.</summary>
    private sealed class StalledWriteException() : IOException("The client stopped reading a preview transfer.");

    private sealed record Offered(long Length, Func<Stream> Open, DateTimeOffset At);

    /// <summary>An instance of the named pipe, restricted to the current user.</summary>
    private sealed class NamedPipeListener(string name) : IPreviewPipeListener
    {
        private readonly NamedPipeServerStream _pipe = new(name, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        public Stream Connection => _pipe;

        public Task WaitForConnectionAsync(CancellationToken stop)
        {
            return _pipe.WaitForConnectionAsync(stop);
        }

        public ValueTask DisposeAsync()
        {
            return _pipe.DisposeAsync();
        }
    }
}
