// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     Projects a campaign model to the filtered graph the webview renders. Shared by
///     <see cref="GetStoryGraphHandler" /> (over the committed model) and
///     <see cref="PreviewStoryGraphHandler" /> (over a model built from the staged working copy),
///     so a preview looks exactly like the committed graph would after Save.
/// </summary>
internal static class StoryGraphProjection
{
    /// <summary>
    ///     Whether a plot is registered as <c>Active_Plot</c> or <c>Suspended_Plot</c> by the
    ///     faction's manifest - a property of the THREAD, not of an event.
    ///     <para>
    ///         Not the same question as an event's lifecycle, which is why it is its own filter: a
    ///         suspended plot's events read Inactive, and so does an event in a running plot whose
    ///         prereq has not fired. Filtering by lifecycle cannot separate the two.
    ///     </para>
    /// </summary>
    private const string Active = "Active";

    private const string Suspended = "Suspended";

    public static GetStoryGraphResult Project(
        StoryCampaignModel model, string? nameFilter, string? branch, string? lifecycle,
        string? reachableFrom, string? plotState = null)
    {
        // Null or empty is every plot the manifest registers, in either state - a suspended plot is
        // part of the chain, waiting for something to resume it.
        var wantSuspended = string.Equals(plotState, Suspended, StringComparison.OrdinalIgnoreCase);
        var wantActive = string.Equals(plotState, Active, StringComparison.OrdinalIgnoreCase);
        var evaluator = new StoryEvaluator(model.Graph);
        var state = StoryRuntimeState.Initial with
        {
            SuspendedThreads = StoryRuntimeState.Initial.SuspendedThreads.Union(model.SuspendedThreadUris)
        };
        var reachable = evaluator.ComputeReachableEvents();

        // Filters select EVENT nodes; virtual nodes and edges survive when both endpoints do.
        var keptEvents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in model.Graph.Nodes.Where(n => n.Kind == StoryNodeKind.Event))
        {
            if (nameFilter is { Length: > 0 } name &&
                !node.Label.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (branch is { Length: > 0 } branchFilter &&
                !string.Equals(node.Event!.Branch, branchFilter, StringComparison.OrdinalIgnoreCase)) continue;
            if (lifecycle is { Length: > 0 } lifecycleFilter &&
                !string.Equals(evaluator.GetLifecycle(node.Id, state).ToString(), lifecycleFilter,
                    StringComparison.OrdinalIgnoreCase)) continue;

            // The node's own thread decides, so a virtual node follows the events it joins - which
            // the pass below already arranges.
            if (wantActive || wantSuspended)
            {
                var suspended = node.ThreadUri is not null
                                && model.SuspendedThreadUris.Contains(node.ThreadUri);
                if (suspended != wantSuspended) continue;
            }

            keptEvents.Add(node.Id);
        }

        if (reachableFrom is { Length: > 0 } fromId)
            keptEvents.IntersectWith(ForwardClosure(model.Graph, fromId));

        var keptNodes = new List<StoryGraphNodeDto>();
        var keptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in model.Graph.Nodes)
        {
            var keep = node.Kind == StoryNodeKind.Event
                ? keptEvents.Contains(node.Id)
                : model.Graph.Edges.Any(e =>
                    (e.FromId == node.Id && keptEvents.Contains(e.ToId)) ||
                    (e.ToId == node.Id && keptEvents.Contains(e.FromId)));
            if (!keep) continue;

            keptIds.Add(node.Id);
            keptNodes.Add(new StoryGraphNodeDto(
                node.Id, node.Kind.ToString(), node.Label, node.ThreadUri,
                node.Event?.NameRange.StartLine,
                node.Event?.EventType, node.Event?.RewardType, node.Event?.Branch,
                node.Kind == StoryNodeKind.Event
                    ? evaluator.GetLifecycle(node.Id, state).ToString()
                    : null,
                node.Kind != StoryNodeKind.Event || reachable.Contains(node.Id),
                node.Event?.EventParams.Select(p => new StoryParamValueDto(p.Position, p.RawValue)).ToList(),
                node.Event?.RewardParams.Select(p => new StoryParamValueDto(p.Position, p.RawValue)).ToList(),
                node.Event?.Perpetual ?? false,
                node.Event?.StoryDialog,
                node.Event?.StoryChapter));
        }

        var edges = model.Graph.Edges
            .Where(e => keptIds.Contains(e.FromId) && keptIds.Contains(e.ToId))
            .Select(e => new StoryGraphEdgeDto(e.FromId, e.ToId, e.Kind.ToString(), e.Label))
            .ToList();

        // Facets describe the CAMPAIGN, so they are read off the whole model rather than off
        // keptNodes - a list of what you could switch to is worthless once the filter has already
        // removed everything you might switch to.
        return new GetStoryGraphResult(keptNodes, edges, null, Facet(model, n => n.Event?.Branch),
            Facet(model, n => n.ThreadUri));
    }

    private static List<string> Facet(StoryCampaignModel model, Func<StoryNode, string?> of)
    {
        return model.Graph.Nodes
            .Select(of)
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // Everything transitively downstream of the given node - "show me what this event leads to".
    private static HashSet<string> ForwardClosure(StoryGraph graph, string fromId)
    {
        var byFrom = graph.Edges.ToLookup(e => e.FromId, StringComparer.Ordinal);
        var closure = new HashSet<string>(StringComparer.Ordinal) { fromId };
        var queue = new Queue<string>([fromId]);
        while (queue.Count > 0)
            foreach (var edge in byFrom[queue.Dequeue()])
                if (closure.Add(edge.ToId))
                    queue.Enqueue(edge.ToId);
        return closure;
    }
}