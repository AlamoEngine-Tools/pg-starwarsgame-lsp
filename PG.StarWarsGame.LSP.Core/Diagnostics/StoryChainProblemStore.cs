// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

public sealed class StoryChainProblemStore : IStoryChainProblemStore
{
    private readonly object _gate = new();

    private IReadOnlyDictionary<string, IReadOnlyList<StoryChainProblem>> _byUri =
        new Dictionary<string, IReadOnlyList<StoryChainProblem>>(StringComparer.OrdinalIgnoreCase);

    public void Replace(IReadOnlyList<StoryChainProblem> problems)
    {
        var byUri = problems
            .Where(p => p.DocumentUri is not null)
            .GroupBy(p => p.DocumentUri!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StoryChainProblem>)g.ToList(),
                StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            _byUri = byUri;
        }
    }

    public IReadOnlyList<StoryChainProblem> GetForDocument(string canonicalUri)
    {
        lock (_gate)
        {
            return _byUri.TryGetValue(canonicalUri, out var list) ? list : [];
        }
    }
}