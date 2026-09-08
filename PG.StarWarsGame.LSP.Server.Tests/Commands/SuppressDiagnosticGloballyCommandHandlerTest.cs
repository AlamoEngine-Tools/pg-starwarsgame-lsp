// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Server.Commands;

namespace PG.StarWarsGame.LSP.Server.Tests.Commands;

public sealed class SuppressDiagnosticGloballyCommandHandlerTest
{
    private static (SuppressDiagnosticGloballyCommandHandler handler, FakeStore store,
        List<FakeRepublisher> republishers) Build()
    {
        var store = new FakeStore();

        // One per language: the store is shared, so a project-wide suppression has to refresh
        // every language's published diagnostics, not just the one the fix was invoked from.
        var republishers = new List<FakeRepublisher> { new(), new(), new() };
        var handler = new SuppressDiagnosticGloballyCommandHandler(
            store, republishers,
            NullLogger<SuppressDiagnosticGloballyCommandHandler>.Instance);
        return (handler, store, republishers);
    }

    private static ExecuteCommandParams Params(params object?[] args)
    {
        return new ExecuteCommandParams
        {
            Command = SuppressDiagnosticGloballyCommandHandler.CommandName,
            Arguments = new JArray(args!)
        };
    }

    [Fact]
    public async Task Handle_StoresTheMatcherForASingleId()
    {
        var (handler, store, _) = Build();

        await handler.Handle(Params("aetswg-004-0001"), CancellationToken.None);

        Assert.Equal(SuppressionMatcher.ForId(new DiagnosticId(4, 1)), Assert.Single(store.Added).Matcher);
    }

    [Fact]
    public async Task Handle_StoresAWholeGroupMatcher()
    {
        var (handler, store, _) = Build();

        await handler.Handle(Params("aetswg-004-*"), CancellationToken.None);

        var added = Assert.Single(store.Added);
        Assert.True(added.Matcher.IsWholeGroup);
        Assert.Equal((int)DiagnosticGroup.Assets, added.Matcher.Group);
    }

    // Published diagnostics do not know the store changed, so without this the suppression would
    // not visibly take effect until each file was next edited.
    [Fact]
    public async Task Handle_RevalidatesSoTheSuppressionTakesEffectImmediately()
    {
        var (handler, _, republishers) = Build();

        await handler.Handle(Params("aetswg-004-0001"), CancellationToken.None);

        Assert.All(republishers, r => Assert.True(r.Called));
    }

    // The failure this prevents: suppressing project-wide from a Lua or dialog file wrote the
    // entry but refreshed only XML, so the diagnostic the user just silenced stayed on screen
    // until they next edited that file.
    [Fact]
    public async Task Handle_RefreshesEveryLanguageNotJustTheOneItWasInvokedFrom()
    {
        var (handler, _, republishers) = Build();

        await handler.Handle(Params("aetswg-004-0001"), CancellationToken.None);

        Assert.Equal(republishers.Count, republishers.Count(r => r.Called));
    }

    // One language failing to refresh must not stop the others: the suppression is already
    // stored, and a half-refreshed screen is better than a half-stored one.
    [Fact]
    public async Task Handle_OneRepublisherThrowing_StillRefreshesTheRest()
    {
        var (handler, _, republishers) = Build();
        republishers[0].Throw = true;

        await handler.Handle(Params("aetswg-004-0001"), CancellationToken.None);

        Assert.True(republishers[1].Called);
        Assert.True(republishers[2].Called);
    }

    // A malformed id must not be guessed at - storing a wrong matcher would silence something the
    // user never asked to silence, and it would be persisted to a committed file.
    [Theory]
    [InlineData("not-an-id")]
    [InlineData("aetswg-4-1")]
    [InlineData("")]
    public async Task Handle_MalformedId_StoresNothing(string raw)
    {
        var (handler, store, republishers) = Build();

        await handler.Handle(Params(raw), CancellationToken.None);

        Assert.Empty(store.Added);
        Assert.All(republishers, r => Assert.False(r.Called));
    }

    [Fact]
    public async Task Handle_NoArguments_StoresNothing()
    {
        var (handler, store, _) = Build();

        await handler.Handle(
            new ExecuteCommandParams { Command = SuppressDiagnosticGloballyCommandHandler.CommandName },
            CancellationToken.None);

        Assert.Empty(store.Added);
    }

    private sealed class FakeStore : IGlobalSuppressionStore
    {
        public List<(SuppressionMatcher Matcher, string? Reason)> Added { get; } = [];

        public IReadOnlyList<SuppressionMatcher> GetAll()
        {
            return Added.Select(a => a.Matcher).ToList();
        }

        public void Add(SuppressionMatcher matcher, string? reason = null)
        {
            Added.Add((matcher, reason));
        }

        public void Remove(SuppressionMatcher matcher)
        {
        }
    }

    private sealed class FakeRepublisher : IDiagnosticsRepublisher
    {
        public bool Called { get; private set; }

        public bool Throw { get; set; }

        public Task RepublishAllAsync(CancellationToken ct)
        {
            Called = true;
            return Throw ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        }
    }
}
