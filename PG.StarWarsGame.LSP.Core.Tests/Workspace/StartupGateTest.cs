// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Workspace;

public sealed class StartupGateTest
{
    [Fact]
    public void IsOpen_IsFalse_ByDefault()
    {
        var gate = new StartupGate();

        Assert.False(gate.IsOpen);
    }

    [Fact]
    public async Task RunOrBufferAsync_WhileBuffering_DoesNotRunActionImmediately()
    {
        var gate = new StartupGate();
        var ran = false;

        await gate.RunOrBufferAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.False(ran);
    }

    [Fact]
    public async Task OpenAsync_DrainsBufferedActions_InArrivalOrder()
    {
        var gate = new StartupGate();
        var order = new List<int>();

        await gate.RunOrBufferAsync(_ =>
        {
            order.Add(1);
            return Task.CompletedTask;
        }, CancellationToken.None);
        await gate.RunOrBufferAsync(_ =>
        {
            order.Add(2);
            return Task.CompletedTask;
        }, CancellationToken.None);
        await gate.RunOrBufferAsync(_ =>
        {
            order.Add(3);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Empty(order);

        await gate.OpenAsync();

        Assert.Equal(new[] { 1, 2, 3 }, order);
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public async Task RunOrBufferAsync_AfterOpen_RunsImmediately()
    {
        var gate = new StartupGate();
        await gate.OpenAsync();

        var ran = false;
        await gate.RunOrBufferAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(ran);
    }

    [Fact]
    public async Task OpenAsync_CalledTwice_DoesNotReRunBufferedActions()
    {
        var gate = new StartupGate();
        var runCount = 0;
        await gate.RunOrBufferAsync(_ =>
        {
            runCount++;
            return Task.CompletedTask;
        }, CancellationToken.None);

        await gate.OpenAsync();
        await gate.OpenAsync();

        Assert.Equal(1, runCount);
    }

    [Fact]
    public async Task OpenAsync_DrainsActionsEnqueuedDuringDrain_AfterOriginalOnes()
    {
        var gate = new StartupGate();
        var order = new List<string>();

        // The first buffered action enqueues a follow-up while the gate is draining.
        await gate.RunOrBufferAsync(_ =>
        {
            order.Add("first");
            return gate.RunOrBufferAsync(__ =>
                {
                    order.Add("nested");
                    return Task.CompletedTask;
                },
                CancellationToken.None);
        }, CancellationToken.None);
        await gate.RunOrBufferAsync(_ =>
        {
            order.Add("second");
            return Task.CompletedTask;
        }, CancellationToken.None);

        await gate.OpenAsync();

        Assert.Equal(new[] { "first", "second", "nested" }, order);
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public async Task OpenAsync_OnEmptyGate_JustOpens()
    {
        var gate = new StartupGate();

        await gate.OpenAsync();

        Assert.True(gate.IsOpen);
    }
}