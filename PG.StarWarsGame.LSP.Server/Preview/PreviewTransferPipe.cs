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
///         is dropped after <see cref="Unclaimed" />, because an offer may hold its payload in
///         memory - a model converted with no project to keep it in, a texture out of an archive.
///     </para>
///     <para>
///         On Windows the client connects to <c>\\.\pipe\&lt;name&gt;</c>. Elsewhere .NET backs a named
///         pipe with a Unix domain socket in the temp directory and the client connects to that path -
///         INFERRED from the runtime's documented behaviour, not run on Linux.
///     </para>
/// </remarks>
public sealed class PreviewTransferPipe(ILogger<PreviewTransferPipe> logger, TimeProvider? time = null)
    : IPreviewTransferChannel, IDisposable
{
    /// <summary>How long an offer nobody collects is kept.</summary>
    public static readonly TimeSpan Unclaimed = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, Offered> _offers = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _startLock = new();
    private bool _listening;

    /// <summary>The pipe's bare name.</summary>
    public string Name { get; } = $"aetswg-preview-{Environment.ProcessId}-{Guid.NewGuid():N}";

    /// <summary>What the client connects to.</summary>
    public string ClientPath => OperatingSystem.IsWindows()
        ? @"\\.\pipe\" + Name
        : Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + Name);

    public PreviewTransfer Offer(string format, long length, Func<Stream> open)
    {
        EnsureListening();
        DropUnclaimed();

        var id = Guid.NewGuid();
        _offers[id] = new Offered(length, open, _time.GetUtcNow());
        return new PreviewTransfer(ClientPath, id.ToString("N"), length, format);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _offers.Clear();
    }

    private void EnsureListening()
    {
        lock (_startLock)
        {
            if (_listening)
                return;

            _listening = true;
        }

        // The first listener exists before Offer returns, so a client that connects the moment it
        // has the reply finds the pipe there.
        var first = Listener();
        _ = Task.Run(() => AcceptAsync(first, _stop.Token));
    }

    private NamedPipeServerStream Listener()
    {
        return new NamedPipeServerStream(Name, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    private async Task AcceptAsync(NamedPipeServerStream listener, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await listener.WaitForConnectionAsync(stop);

                var connection = listener;
                listener = Listener();
                _ = Task.Run(() => ServeAsync(connection, stop), stop);
            }
        }
        catch (OperationCanceledException)
        {
            // The server is stopping.
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "The preview transfer pipe {Pipe} stopped listening", Name);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    private async Task ServeAsync(NamedPipeServerStream connection, CancellationToken stop)
    {
        var request = new byte[PreviewTransferFraming.RequestLength];
        var header = new byte[PreviewTransferFraming.ResponseHeaderLength];

        try
        {
            await using (connection)
            {
                while (await connection.ReadAtLeastAsync(request, request.Length, false, stop) == request.Length)
                {
                    // Big-endian: the id travels as the bytes its hex digits spell, so a client
                    // needs no knowledge of how a GUID is laid out in memory.
                    var (status, length, payload) = Take(new Guid(request, true));
                    await using (payload)
                    {
                        PreviewTransferFraming.WriteResponseHeader(header, status, length);
                        await connection.WriteAsync(header, stop);

                        if (payload is not null)
                            await CopyExactlyAsync(payload, connection, length, stop);

                        await connection.FlushAsync(stop);
                    }
                }
            }
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

    private void DropUnclaimed()
    {
        var before = _time.GetUtcNow() - Unclaimed;
        foreach (var (id, offered) in _offers)
            if (offered.At < before)
                _offers.TryRemove(id, out _);
    }

    /// <summary>
    ///     Sends exactly the bytes the header announced. A source that ends early - a file that
    ///     shrank between the offer and the collection - cannot fill the frame, so the connection is
    ///     given up: the client sees it close mid-payload, which it reports, instead of reading the
    ///     next frame out of step.
    /// </summary>
    private static async Task CopyExactlyAsync(Stream source, Stream target, long length, CancellationToken stop)
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

            await target.WriteAsync(buffer.AsMemory(0, read), stop);
            left -= read;
        }
    }

    private sealed record Offered(long Length, Func<Stream> Open, DateTimeOffset At);
}