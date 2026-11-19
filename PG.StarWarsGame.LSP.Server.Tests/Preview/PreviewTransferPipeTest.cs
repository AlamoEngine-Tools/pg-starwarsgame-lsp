// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Pipes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The preview's transfer pipe (#436) and the frames on it. <c>ProtocolMethodContractTest</c>
///     cannot see this channel - it is not JSON-RPC - so its contract is pinned here; the client's
///     half is pinned by <c>previewTransfer.test.ts</c> against the same bytes.
/// </summary>
public sealed class PreviewTransferPipeTest
{
    private static PreviewTransferPipe Pipe(TimeProvider? time = null)
    {
        return new PreviewTransferPipe(NullLogger<PreviewTransferPipe>.Instance, time);
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(PreviewTransferPipe pipe)
    {
        var client = new NamedPipeClientStream(".", pipe.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(10_000, TestContext.Current.CancellationToken);
        return client;
    }

    /// <summary>One exchange, as a client makes it: the id's sixteen bytes out, a header and the payload in.</summary>
    private static async Task<(byte Status, byte[] Payload)> AskAsync(Stream connection, string id)
    {
        var stop = TestContext.Current.CancellationToken;
        await connection.WriteAsync(Convert.FromHexString(id), stop);
        await connection.FlushAsync(stop);

        var header = new byte[PreviewTransferFraming.ResponseHeaderLength];
        await connection.ReadExactlyAsync(header, stop);
        var (status, length) = PreviewTransferFraming.ReadResponseHeader(header);

        var payload = new byte[length];
        await connection.ReadExactlyAsync(payload, stop);
        return (status, payload);
    }

    private static PreviewTransfer Offer(PreviewTransferPipe pipe, byte[] bytes)
    {
        return pipe.Offer("glb", bytes.Length, () => new MemoryStream(bytes, false));
    }

    // ── the frames ────────────────────────────────────────────────────────────

    [Fact]
    public void ResponseHeader_IsTheStatusByteAndTheLengthLittleEndian()
    {
        var header = new byte[PreviewTransferFraming.ResponseHeaderLength];

        PreviewTransferFraming.WriteResponseHeader(header, PreviewTransferFraming.Ok, 0x0102030405);

        Assert.Equal([0, 0x05, 0x04, 0x03, 0x02, 0x01, 0, 0, 0], header);
        Assert.Equal((PreviewTransferFraming.Ok, 0x0102030405), PreviewTransferFraming.ReadResponseHeader(header));
    }

    // ── the offer ─────────────────────────────────────────────────────────────

    [Fact]
    public void Offer_NamesThePipeAnIdOfThirtyTwoHexDigitsTheLengthAndTheFormat()
    {
        using var pipe = Pipe();

        var transfer = Offer(pipe, [1, 2, 3]);

        Assert.Equal(pipe.ClientPath, transfer.Pipe);
        Assert.EndsWith(pipe.Name, transfer.Pipe);
        Assert.Matches("^[0-9a-f]{32}$", transfer.Id);
        Assert.Equal(3, transfer.Length);
        Assert.Equal("glb", transfer.Format);
    }

    [Fact]
    public void Offer_OnWindows_NamesAPipePath()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("The Windows form of the path.");

        using var pipe = Pipe();

        Assert.StartsWith(@"\\.\pipe\aetswg-preview-", Offer(pipe, [1]).Pipe);
    }

    // ── the exchange ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Ask_ForAnOfferedTransfer_GetsItsLengthAndItsBytes()
    {
        using var pipe = Pipe();
        var bytes = Enumerable.Range(0, 5_000_000).Select(i => (byte)(i * 31)).ToArray();
        var transfer = Offer(pipe, bytes);

        await using var connection = await ConnectAsync(pipe);
        var (status, payload) = await AskAsync(connection, transfer.Id);

        Assert.Equal(PreviewTransferFraming.Ok, status);
        Assert.Equal(bytes, payload);
    }

    [Fact]
    public async Task Ask_ForATransferNobodyOffered_IsToldSoAndKeepsTheConnection()
    {
        using var pipe = Pipe();
        var transfer = Offer(pipe, [7, 7]);

        await using var connection = await ConnectAsync(pipe);
        var unknown = await AskAsync(connection, Guid.NewGuid().ToString("N"));
        var known = await AskAsync(connection, transfer.Id);

        Assert.Equal(PreviewTransferFraming.Unknown, unknown.Status);
        Assert.Empty(unknown.Payload);
        Assert.Equal(PreviewTransferFraming.Ok, known.Status);
        Assert.Equal([7, 7], known.Payload);
    }

    [Fact]
    public async Task ATransfer_IsCollectedOnce()
    {
        using var pipe = Pipe();
        var transfer = Offer(pipe, [7, 7]);

        await using var connection = await ConnectAsync(pipe);
        await AskAsync(connection, transfer.Id);
        var again = await AskAsync(connection, transfer.Id);

        Assert.Equal(PreviewTransferFraming.Unknown, again.Status);
    }

    [Fact]
    public async Task TwoTransfers_OverTwoConnectionsAtOnce_EachGetTheirOwnBytes()
    {
        using var pipe = Pipe();
        var first = Offer(pipe, [1, 1, 1]);
        var second = Offer(pipe, [2, 2]);

        await using var a = await ConnectAsync(pipe);
        await using var b = await ConnectAsync(pipe);
        var answers = await Task.WhenAll(AskAsync(b, second.Id), AskAsync(a, first.Id));

        Assert.Equal([2, 2], answers[0].Payload);
        Assert.Equal([1, 1, 1], answers[1].Payload);
    }

    /// <summary>The payload is opened when the client asks, and closed when it has been sent.</summary>
    [Fact]
    public async Task Ask_OpensThePayloadThenAndClosesItAfterwards()
    {
        using var pipe = Pipe();
        var payload = new TrackedStream([4, 5, 6]);
        var opened = 0;
        var transfer = pipe.Offer("glb", 3, () =>
        {
            opened++;
            return payload;
        });

        Assert.Equal(0, opened);

        await using var connection = await ConnectAsync(pipe);
        await AskAsync(connection, transfer.Id);
        // The header of a second exchange is written after the first payload was closed.
        await AskAsync(connection, transfer.Id);

        Assert.Equal(1, opened);
        Assert.True(payload.Closed);
    }

    /// <summary>A cache file deleted between the reply and the collection: said, not thrown.</summary>
    [Fact]
    public async Task Ask_ForATransferWhosePayloadIsGone_IsToldSo()
    {
        using var pipe = Pipe();
        var transfer = pipe.Offer("glb", 3, () => throw new FileNotFoundException("cleared"));

        await using var connection = await ConnectAsync(pipe);
        var answer = await AskAsync(connection, transfer.Id);

        Assert.Equal(PreviewTransferFraming.Gone, answer.Status);
        Assert.Empty(answer.Payload);
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes, false)
    {
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Closed { get; private set; }

        public Task WhenClosed => _closed.Task;

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            _closed.TrySetResult();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///     Thirty seconds, not minutes: with no project to hold a cache, an offer's payload is the
    ///     whole converted model in the server's memory until somebody collects it.
    /// </summary>
    [Fact]
    public async Task AnOfferNobodyCollects_IsDroppedAfterThirtySeconds()
    {
        var clock = new Clock();
        using var pipe = Pipe(clock);
        var stale = Offer(pipe, [1]);
        clock.Advance(TimeSpan.FromSeconds(29));
        var fresh = Offer(pipe, [2]);
        clock.Advance(TimeSpan.FromSeconds(2));

        // The next offer is what sweeps: the first is 31 s old by now, the second 2 s.
        Offer(pipe, [3]);

        await using var connection = await ConnectAsync(pipe);
        Assert.Equal(PreviewTransferFraming.Unknown, (await AskAsync(connection, stale.Id)).Status);
        Assert.Equal(PreviewTransferFraming.Ok, (await AskAsync(connection, fresh.Id)).Status);
        Assert.Equal(TimeSpan.FromSeconds(30), PreviewTransferPipe.Unclaimed);
    }

    /// <summary>
    ///     A texture is offered by its file's length and read when it is collected. A file that got
    ///     shorter in between cannot fill the frame that was announced; the connection is closed
    ///     rather than the next frame being read out of step.
    /// </summary>
    [Fact]
    public async Task APayloadShorterThanItWasOffered_ClosesTheConnection()
    {
        using var pipe = Pipe();
        var transfer = pipe.Offer("dds", 10, () => new MemoryStream([1, 2, 3], false));

        await using var connection = await ConnectAsync(pipe);
        await connection.WriteAsync(Convert.FromHexString(transfer.Id), TestContext.Current.CancellationToken);

        var received = new byte[64];
        var total = 0;
        int read;
        while ((read = await connection.ReadAsync(received.AsMemory(total), TestContext.Current.CancellationToken)) > 0)
            total += read;

        // The header announcing ten bytes, the three there were, and then the end of the stream.
        Assert.Equal(PreviewTransferFraming.ResponseHeaderLength + 3, total);
    }

    /// <summary>A payload LONGER than offered is cut at the length announced, and the connection lives on.</summary>
    [Fact]
    public async Task APayloadLongerThanItWasOffered_IsCutAtTheLengthAnnounced()
    {
        using var pipe = Pipe();
        var transfer = pipe.Offer("dds", 2, () => new MemoryStream([1, 2, 3, 4], false));
        var next = Offer(pipe, [9]);

        await using var connection = await ConnectAsync(pipe);
        var cut = await AskAsync(connection, transfer.Id);
        var after = await AskAsync(connection, next.Id);

        Assert.Equal([1, 2], cut.Payload);
        Assert.Equal([9], after.Payload);
    }

    // ── an offer nobody collects ──────────────────────────────────────────────

    /// <summary>
    ///     The payload of an offer may be a whole converted model in memory. It goes when its time
    ///     is up, whether or not another offer ever comes to sweep it out.
    /// </summary>
    [Fact]
    public async Task AnOfferNobodyCollects_IsDroppedWhenItsTimeIsUp_WithoutAnotherOffer()
    {
        var clock = new FakeTimeProvider();
        using var pipe = Pipe(clock);
        var stale = Offer(pipe, [1]);
        clock.Advance(TimeSpan.FromSeconds(20));
        var fresh = Offer(pipe, [2]);

        clock.Advance(TimeSpan.FromSeconds(11));

        await using var connection = await ConnectAsync(pipe);
        Assert.Equal(PreviewTransferFraming.Unknown, (await AskAsync(connection, stale.Id)).Status);

        // The second is 11 s old, and goes in its own time: 30 s after it was offered.
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(PreviewTransferFraming.Unknown, (await AskAsync(connection, fresh.Id)).Status);
    }

    // ── a listener that breaks ────────────────────────────────────────────────

    /// <summary>
    ///     One instance of the pipe failing while it waits is not the end of the channel: it is
    ///     replaced, and a transfer offered before it broke is still collected.
    /// </summary>
    [Fact]
    public async Task AListenerThatBreaks_IsReplaced_AndTheOfferIsStillCollected()
    {
        var opened = 0;
        using var pipe = new PreviewTransferPipe(NullLogger<PreviewTransferPipe>.Instance, null,
            name => Interlocked.Increment(ref opened) == 1 ? new BrokenListener() : new PipeListener(name));
        var transfer = Offer(pipe, [7, 7]);

        await using var connection = await ConnectAsync(pipe);
        var answer = await AskAsync(connection, transfer.Id);

        Assert.Equal(PreviewTransferFraming.Ok, answer.Status);
        Assert.Equal([7, 7], answer.Payload);
    }

    /// <summary>
    ///     A pipe whose every instance breaks is given up after a few tries rather than spun on -
    ///     and the next offer opens it again, so no offer names a pipe nothing listens on.
    /// </summary>
    [Fact]
    public async Task AListenerThatKeepsBreaking_IsGivenUp_AndTheNextOfferListensAgain()
    {
        var repaired = new TaskCompletionSource();
        var opened = 0;
        using var pipe = new PreviewTransferPipe(NullLogger<PreviewTransferPipe>.Instance, null, name =>
        {
            Interlocked.Increment(ref opened);
            return repaired.Task.IsCompleted ? new PipeListener(name) : new BrokenListener();
        });

        Offer(pipe, [1]);
        await Soon(pipe.Accepting);
        Assert.Equal(PreviewTransferPipe.ListenAttempts, opened);

        repaired.SetResult();
        var transfer = Offer(pipe, [7, 7]);

        await using var connection = await ConnectAsync(pipe);
        Assert.Equal([7, 7], (await AskAsync(connection, transfer.Id)).Payload);
    }

    /// <summary>With no instance of the pipe to be had, an offer fails instead of naming one.</summary>
    [Fact]
    public void WhenThePipeCannotBeOpened_EveryOfferFails()
    {
        using var pipe = new PreviewTransferPipe(NullLogger<PreviewTransferPipe>.Instance, null,
            _ => throw new IOException("All pipe instances are busy."));

        Assert.Throws<IOException>(() => Offer(pipe, [1]));
        Assert.Throws<IOException>(() => Offer(pipe, [2]));
    }

    // ── a client that stops reading ───────────────────────────────────────────

    /// <summary>
    ///     A payload is open while it is sent - a texture's own file, for one. A client that stops
    ///     reading must not keep it open: the write is given up after
    ///     <see cref="PreviewTransferPipe.WriteStall" /> and the payload closed.
    /// </summary>
    [Fact]
    public async Task AClientThatStopsReading_IsGivenUp_AndThePayloadClosed()
    {
        var clock = new FakeTimeProvider();
        var client = new StalledClient();
        var opened = 0;
        using var pipe = new PreviewTransferPipe(NullLogger<PreviewTransferPipe>.Instance, clock,
            _ => Interlocked.Increment(ref opened) == 1 ? client : new IdleListener());
        var payload = new TrackedStream(new byte[1024]);
        var transfer = pipe.Offer("dds", 1024, () => payload);

        client.Ask(transfer.Id);
        await Soon(client.Stalled);
        Assert.False(payload.Closed);

        clock.Advance(PreviewTransferPipe.WriteStall);

        await Soon(payload.WhenClosed);
        Assert.True(payload.Closed);
    }

    private static async Task Soon(Task task)
    {
        Assert.Same(task, await Task.WhenAny(task,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)));
    }

    /// <summary>
    ///     The limit is on one write waiting, not on the transfer. A stall timer that fires just as
    ///     its write completes belongs to that write alone: the rest of the response, and the next
    ///     exchange on the connection, go through.
    /// </summary>
    [Fact]
    public async Task AStallTimerFiringAsItsWriteCompletes_LeavesTheNextExchangeWorking()
    {
        var clock = new FakeTimeProvider();
        var client = new ScriptedClient();
        var opened = 0;
        using var pipe = new PreviewTransferPipe(NullLogger<PreviewTransferPipe>.Instance, clock,
            _ => Interlocked.Increment(ref opened) == 1 ? client : new IdleListener());
        var first = Offer(pipe, [1, 2, 3]);

        // The payload's write is taken whole - and its time runs out before it returns.
        client.OnWrite = (index, _) =>
        {
            if (index == 1)
                clock.Advance(PreviewTransferPipe.WriteStall);
            return Task.CompletedTask;
        };

        client.Ask(first.Id);
        var one = await client.TakeAsync(PreviewTransferFraming.ResponseHeaderLength + 3);

        // Offered now: the clock has moved on by as long as an unclaimed offer is kept.
        var second = Offer(pipe, [4, 5]);
        client.Ask(second.Id);
        var two = await client.TakeAsync(PreviewTransferFraming.ResponseHeaderLength + 2);

        Assert.Equal([1, 2, 3], one[PreviewTransferFraming.ResponseHeaderLength..]);
        Assert.Equal(PreviewTransferFraming.Ok, two[0]);
        Assert.Equal([4, 5], two[PreviewTransferFraming.ResponseHeaderLength..]);
    }

    /// <summary>
    ///     A large payload to a client that reads slowly, but reads, is not cut off: every write
    ///     here takes twenty seconds and the transfer far longer than the limit on one of them.
    /// </summary>
    [Fact]
    public async Task AClientThatReadsSlowly_GetsTheWholePayload()
    {
        var clock = new FakeTimeProvider();
        var client = new ScriptedClient();
        var opened = 0;
        using var pipe = new PreviewTransferPipe(NullLogger<PreviewTransferPipe>.Instance, clock,
            _ => Interlocked.Increment(ref opened) == 1 ? client : new IdleListener());
        var bytes = Enumerable.Range(0, 600_000).Select(i => (byte)(i * 7)).ToArray();
        var transfer = Offer(pipe, bytes);
        var waiting = Channel.CreateUnbounded<TaskCompletionSource>();
        client.OnWrite = async (_, stop) =>
        {
            var taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await waiting.Writer.WriteAsync(taken, stop);
            await taken.Task.WaitAsync(stop);
        };

        client.Ask(transfer.Id);
        var received = client.TakeAsync(PreviewTransferFraming.ResponseHeaderLength + bytes.Length);
        var writes = 0;
        while (!received.IsCompleted)
        {
            var next = waiting.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask();
            if (await Task.WhenAny(next, received) == received)
                break;

            clock.Advance(TimeSpan.FromSeconds(20));
            (await next).SetResult();
            writes++;
        }

        Assert.True(writes * 20 > PreviewTransferPipe.WriteStall.TotalSeconds * 2);
        Assert.Equal(bytes, (await received)[PreviewTransferFraming.ResponseHeaderLength..]);
    }

    /// <summary>
    ///     A connected client the test drives: it sends the ids it is told to, and takes each write
    ///     through <see cref="OnWrite" /> - which may hold it, or move the clock - before keeping it.
    /// </summary>
    private sealed class ScriptedClient : Stream, IPreviewPipeListener
    {
        private readonly Channel<byte[]> _requests = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> _written = Channel.CreateUnbounded<byte[]>();
        private readonly List<byte> _taken = [];
        private int _writes;

        /// <summary>Called with each write's index, from zero, before the write is kept.</summary>
        public Func<int, CancellationToken, Task> OnWrite { get; set; } = (_, _) => Task.CompletedTask;

        public Stream Connection => this;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public Task WaitForConnectionAsync(CancellationToken stop)
        {
            return Task.CompletedTask;
        }

        public void Ask(string id)
        {
            _requests.Writer.TryWrite(Convert.FromHexString(id));
        }

        /// <summary>The next <paramref name="count" /> bytes the server wrote.</summary>
        public async Task<byte[]> TakeAsync(int count)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(20));
            while (_taken.Count < count)
                _taken.AddRange(await _written.Reader.ReadAsync(limit.Token));

            var bytes = _taken.Take(count).ToArray();
            _taken.RemoveRange(0, count);
            return bytes;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var id = await _requests.Reader.ReadAsync(cancellationToken);
            id.CopyTo(buffer);
            return id.Length;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kept = buffer.ToArray();
            await OnWrite(Interlocked.Increment(ref _writes) - 1, cancellationToken);
            _written.Writer.TryWrite(kept);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>An instance of the pipe that fails as soon as it is waited on.</summary>
    private sealed class BrokenListener : IPreviewPipeListener
    {
        public Stream Connection => Stream.Null;

        public Task WaitForConnectionAsync(CancellationToken stop)
        {
            return Task.FromException(new IOException("The pipe is broken."));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>An instance of the pipe nobody connects to.</summary>
    private sealed class IdleListener : IPreviewPipeListener
    {
        public Stream Connection => Stream.Null;

        public Task WaitForConnectionAsync(CancellationToken stop)
        {
            return Task.Delay(Timeout.Infinite, stop);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A real instance of the named pipe.</summary>
    private sealed class PipeListener(string name) : IPreviewPipeListener
    {
        private readonly NamedPipeServerStream _pipe = new(name, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

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

    /// <summary>
    ///     A client that connects when the test asks, sends one transfer id, takes the response
    ///     header and then reads nothing more: every later write waits until it is cancelled.
    /// </summary>
    private sealed class StalledClient : Stream, IPreviewPipeListener
    {
        private readonly TaskCompletionSource<byte[]> _asked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _sent;
        private int _writes;

        /// <summary>Completes when a write is waiting on this client.</summary>
        public Task Stalled => _stalled.Task;

        public Stream Connection => this;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public async Task WaitForConnectionAsync(CancellationToken stop)
        {
            await _asked.Task.WaitAsync(stop);
        }

        public void Ask(string id)
        {
            _asked.TrySetResult(Convert.FromHexString(id));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_sent)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            _sent = true;
            var id = await _asked.Task;
            id.CopyTo(buffer);
            return id.Length;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            // The header is taken; the payload is not.
            if (Interlocked.Increment(ref _writes) == 1)
                return;

            _stalled.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
        }
    }
}
