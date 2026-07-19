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
    public static GetStoryGraphResult Project(
        StoryCampaignModel model, string? nameFilter, string? branch, string? lifecycle, string? reachableFrom)
    {
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

        return new GetStoryGraphResult(keptNodes, edges);
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