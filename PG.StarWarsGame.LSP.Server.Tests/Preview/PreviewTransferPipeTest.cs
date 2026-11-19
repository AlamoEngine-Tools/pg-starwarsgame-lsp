// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Pipes;
using Microsoft.Extensions.Logging.Abstractions;
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
        public bool Closed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
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