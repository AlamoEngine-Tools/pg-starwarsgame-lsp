// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     A thing derived from an index snapshot lives with that snapshot (#311): the same key gives the
///     same instance, another snapshot starts with none, and the first callers racing for a key run
///     its factory once.
/// </summary>
public sealed class GameIndexDerivedTest
{
    private static GameIndex Index()
    {
        return GameIndex.Empty with { Documents = ImmutableDictionary<string, DocumentIndex>.Empty };
    }

    [Fact]
    public void TheSameKey_GivesTheSameInstance_AndRunsTheFactoryOnce()
    {
        var index = Index();
        var runs = 0;

        var first = index.Derived("catalog", _ => new List<string> { $"run {++runs}" });
        var second = index.Derived("catalog", _ => new List<string> { $"run {++runs}" });

        Assert.Same(first, second);
        Assert.Equal(1, runs);
        Assert.Equal(["run 1"], first);
    }

    [Fact]
    public void DifferentKeys_GiveDifferentThings()
    {
        var index = Index();

        var a = index.Derived("a", _ => new object());
        var b = index.Derived("b", _ => new object());

        Assert.NotSame(a, b);
        Assert.Equal(2, index.DerivedCount);
    }

    [Fact]
    public void TheFactory_IsHandedTheSnapshotItself()
    {
        var index = Index();

        var handed = index.Derived("self", i => i);

        Assert.Same(index, handed);
    }

    [Fact]
    public void AnotherSnapshot_StartsWithNothingDerived()
    {
        var index = Index();
        index.Derived("catalog", _ => new object());

        var copy = index with { Documents = index.Documents };

        Assert.Equal(1, index.DerivedCount);
        Assert.Equal(0, copy.DerivedCount);
        Assert.False(copy.HasDerived("catalog"));
        Assert.NotSame(index.Derived("catalog", _ => new object()), copy.Derived("catalog", _ => new object()));
    }

    [Fact]
    public void HasDerived_SaysWhetherTheThingExists_WithoutMakingIt()
    {
        var index = Index();

        Assert.False(index.HasDerived("catalog"));
        index.Derived("catalog", _ => new object());

        Assert.True(index.HasDerived("catalog"));
        Assert.False(index.HasDerived("other"));
    }

    [Fact]
    public async Task ParallelFirstCallers_RunTheFactoryOnce_AndShareTheResult()
    {
        var index = Index();
        var runs = 0;
        var gate = new TaskCompletionSource();

        var callers = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return index.Derived("shared", _ =>
            {
                Interlocked.Increment(ref runs);
                Thread.Sleep(10);
                return new object();
            });
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, runs);
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Equal(1, index.DerivedCount);
    }
}