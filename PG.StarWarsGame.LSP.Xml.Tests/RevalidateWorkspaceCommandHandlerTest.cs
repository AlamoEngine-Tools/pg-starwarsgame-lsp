// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Commands;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class RevalidateWorkspaceCommandHandlerTest
{
    [Fact]
    public async Task Handle_CallsPublisherRevalidateWorkspace()
    {
        var fake = new FakeRepublisher();
        var handler = new RevalidateWorkspaceCommandHandler([fake]);

        await handler.ExecuteAsync(CancellationToken.None);

        Assert.True(fake.Called);
    }

    // The failure this prevents: the command is offered as "re-runs all diagnostics across indexed
    // files", but it only ever refreshed XML - so running it to clear a stale Lua or dialog problem
    // reported success while re-checking nothing.
    [Fact]
    public async Task Handle_RefreshesEveryLanguage()
    {
        var fakes = new List<FakeRepublisher> { new(), new(), new() };
        var handler = new RevalidateWorkspaceCommandHandler(fakes);

        await handler.ExecuteAsync(CancellationToken.None);

        Assert.All(fakes, f => Assert.True(f.Called));
    }

    // One language failing must not abandon the rest of the sweep.
    [Fact]
    public async Task Handle_OneRepublisherThrowing_StillRefreshesTheRest()
    {
        var fakes = new List<FakeRepublisher> { new() { Throw = true }, new(), new() };
        var handler = new RevalidateWorkspaceCommandHandler(fakes);

        await handler.ExecuteAsync(CancellationToken.None);

        Assert.True(fakes[1].Called);
        Assert.True(fakes[2].Called);
    }

    private sealed class FakeRepublisher : IDiagnosticsRepublisher
    {
        public bool Called { get; private set; }

        public bool Throw { get; init; }

        public Task RepublishAllAsync(CancellationToken ct)
        {
            Called = true;
            return Throw ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        }
    }
}
