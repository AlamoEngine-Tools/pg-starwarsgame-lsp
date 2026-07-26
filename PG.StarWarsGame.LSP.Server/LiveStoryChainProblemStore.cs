// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Story.Discovery;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     Serves story-chain problems from the LIVE chain scan held by
///     <see cref="IStoryModelService" />, which reads open buffers first and re-runs whenever any
///     file it read changes version - so a broken <c>*_Story_Name</c> or plot entry is reported
///     while it is being typed.
///     <para>
///         <see cref="Replace" /> keeps taking the snapshot <c>WorkspaceIndexer.PreScanMetafiles</c>
///         produces, but that snapshot only answers until the live scan can read anything: it is
///         refreshed on startup and <c>.pgproj</c> reload ONLY, so serving it afterwards means
///         reporting problems the user has already fixed and staying silent about new ones.
///     </para>
///     Gated by <c>features.story.discovery</c>, the same flag that gates the startup scan.
/// </summary>
public sealed class LiveStoryChainProblemStore : IStoryChainProblemStore
{
    private readonly ILspConfigurationProvider _configProvider;

    // Lazily resolved: IStoryModelService -> IModProjectReloadService -> IWorkspaceIndexer ->
    // IStoryChainProblemStore, so taking the service in the constructor would close a DI cycle.
    private readonly Func<IStoryModelService> _modelService;
    private readonly StoryChainProblemStore _startupSnapshot = new();

    private readonly object _gate = new();
    private StoryChainScanResult? _cachedFor;
    private IReadOnlyDictionary<string, IReadOnlyList<StoryChainProblem>> _byUri =
        new Dictionary<string, IReadOnlyList<StoryChainProblem>>(StringComparer.OrdinalIgnoreCase);

    public LiveStoryChainProblemStore(
        Func<IStoryModelService> modelService, ILspConfigurationProvider configProvider)
    {
        _modelService = modelService;
        _configProvider = configProvider;
    }

    public void Replace(IReadOnlyList<StoryChainProblem> problems)
    {
        _startupSnapshot.Replace(problems);
    }

    public IReadOnlyList<StoryChainProblem> GetForDocument(string canonicalUri)
    {
        if (!_configProvider.Current.Features.Story.Discovery) return [];

        var result = _modelService().GetChainResult();

        // A scan that reached nothing at all has not run yet (inside the startup window the
        // workspace config and schema are not published, so every read fails). Only then is the
        // snapshot the better answer - once the chain resolves, an empty problem list is a real
        // "no problems" and must not be overridden by a stale one.
        if (ReachedNothing(result)) return _startupSnapshot.GetForDocument(canonicalUri);

        return GroupedBy(result).TryGetValue(canonicalUri, out var problems) ? problems : [];
    }

    private static bool ReachedNothing(StoryChainScanResult result)
    {
        return result.Campaigns.Count == 0
               && result.ManifestFiles.Count == 0
               && result.ThreadFiles.Count == 0
               && result.Problems.Count == 0;
    }

    // The chain result is cached by reference in StoryModelService, so the per-URI grouping is
    // rebuilt only when the scan itself changed - not once per open document per publish.
    private IReadOnlyDictionary<string, IReadOnlyList<StoryChainProblem>> GroupedBy(
        StoryChainScanResult result)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_cachedFor, result)) return _byUri;

            _byUri = result.Problems
                .Where(p => p.DocumentUri is not null)
                .GroupBy(p => p.DocumentUri!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<StoryChainProblem>)g.ToList(),
                    StringComparer.OrdinalIgnoreCase);
            _cachedFor = result;
            return _byUri;
        }
    }
}
