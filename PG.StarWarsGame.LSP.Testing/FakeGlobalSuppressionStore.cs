// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>The project-wide suppressions, held in memory: empty unless a test adds some.</summary>
public sealed class FakeGlobalSuppressionStore : IGlobalSuppressionStore
{
    private readonly List<SuppressionMatcher> _matchers = [];

    public FakeGlobalSuppressionStore(params IEnumerable<SuppressionMatcher> matchers)
    {
        _matchers.AddRange(matchers);
    }

    /// <summary>The reasons given to <see cref="Add" />, in order.</summary>
    public List<string?> Reasons { get; } = [];

    public IReadOnlyList<SuppressionMatcher> GetAll()
    {
        return _matchers;
    }

    public void Add(SuppressionMatcher matcher, string? reason = null)
    {
        _matchers.Add(matcher);
        Reasons.Add(reason);
    }

    public void Remove(SuppressionMatcher matcher)
    {
        _matchers.Remove(matcher);
    }
}