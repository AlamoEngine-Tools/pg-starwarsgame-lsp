// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Graph;

/// <summary>
///     Computes the graph-level diagnostics of one campaign model for one document: resolution
///     problems (dangling prereqs, unresolved/ambiguous control targets), duplicate event names
///     per file, prerequisite cycles, unreachable events, suspended threads that nothing
///     activates, canonical tag-order violations, and over-long flag names.
/// </summary>
public sealed class StoryGraphDiagnosticsProducer(ISchemaProvider schema)
{
    private const int MaxFlagNameLength = 31;

    // Documented event block order ("Story Mode and Tutorial Scripting System"); tags not listed
    // here carry no order constraint. Param slots share their prefix's rank.
    private static readonly string[] CanonicalOrder =
    [
        "EVENT_TYPE", "EVENT_PARAM", "EVENT_FILTER", "REWARD_TYPE", "REWARD_PARAM", "PREREQ",
        "STORY_DIALOG", "STORY_CHAPTER", "STORY_TAG", "STORY_VAR", "BRANCH", "PERPETUAL",
        "MULTIPLAYER", "STORY_DIALOG_POPUP"
    ];

    public IReadOnlyList<StoryGraphDiagnostic> Produce(StoryCampaignModel model, string documentUri)
    {
        var diagnostics = new List<StoryGraphDiagnostic>();
        var thread = model.Threads.FirstOrDefault(t =>
            string.Equals(t.DocumentUri, documentUri, StringComparison.Ordinal));

        foreach (var problem in model.Graph.Problems)
            if (string.Equals(problem.DocumentUri, documentUri, StringComparison.Ordinal))
                diagnostics.Add(At(problem.Range, problem.Message, problem.Kind switch
                {
                    StoryGraphProblemKind.AmbiguousTarget => XmlDiagnosticSeverity.Warning,
                    _ => XmlDiagnosticSeverity.Error
                }));

        if (thread is not null)
        {
            AddDuplicateNames(diagnostics, thread);

            var evaluator = new StoryEvaluator(model.Graph);
            var cycleMembers = AddPrereqCycles(diagnostics, model, evaluator, documentUri);
            AddUnreachableEvents(diagnostics, model, evaluator, documentUri, cycleMembers);
            AddSuspendedNeverActivated(diagnostics, model, documentUri);
            AddTagOrderViolations(diagnostics, thread);
            AddOverlongFlagNames(diagnostics, thread);
        }

        return diagnostics;
    }

    private static void AddDuplicateNames(List<StoryGraphDiagnostic> diagnostics, StoryThread thread)
    {
        foreach (var group in thread.Events
                     .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        foreach (var storyEvent in group)
            diagnostics.Add(At(storyEvent.NameRange,
                $"Event name '{storyEvent.Name}' is defined {group.Count()} times in this file.",
                XmlDiagnosticSeverity.Error));
    }

    private static HashSet<string> AddPrereqCycles(List<StoryGraphDiagnostic> diagnostics,
        StoryCampaignModel model, StoryEvaluator evaluator, string documentUri)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        var nodesById = model.Graph.Nodes
            .Where(n => n.Kind == StoryNodeKind.Event)
            .ToDictionary(n => n.Id, StringComparer.Ordinal);

        foreach (var cycle in evaluator.FindPrereqCycles())
        {
            members.UnionWith(cycle);
            var names = string.Join(" → ", cycle.Select(id => nodesById[id].Label));
            foreach (var id in cycle)
            {
                var node = nodesById[id];
                if (node.ThreadUri != documentUri) continue;
                diagnostics.Add(At(node.Event!.NameRange,
                    $"'{node.Label}' is part of a prerequisite cycle ({names}) — none of these events can arm.",
                    XmlDiagnosticSeverity.Warning));
            }
        }

        return members;
    }

    private static void AddUnreachableEvents(List<StoryGraphDiagnostic> diagnostics,
        StoryCampaignModel model, StoryEvaluator evaluator, string documentUri,
        HashSet<string> cycleMembers)
    {
        // Suspended threads are inactive wholesale — flagging every event inside them is noise.
        if (model.SuspendedThreadUris.Contains(documentUri)) return;

        var reachable = evaluator.ComputeReachableEvents();
        foreach (var node in model.Graph.Nodes)
        {
            if (node.Kind != StoryNodeKind.Event || node.ThreadUri != documentUri) continue;
            if (reachable.Contains(node.Id) || cycleMembers.Contains(node.Id)) continue;
            diagnostics.Add(At(node.Event!.NameRange,
                $"'{node.Label}' can never fire: no prerequisite line is satisfiable and nothing triggers it.",
                XmlDiagnosticSeverity.Warning));
        }
    }

    private static void AddSuspendedNeverActivated(List<StoryGraphDiagnostic> diagnostics,
        StoryCampaignModel model, string documentUri)
    {
        if (!model.SuspendedThreadUris.Contains(documentUri)) return;

        var fileName = documentUri[(documentUri.LastIndexOf('/') + 1)..];
        var plotName = fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;

        // Attached Lua scripts commonly drive suspended plots (Lua analysis lands with Issue 3),
        // so a same-named script suppresses the finding instead of guessing.
        if (model.LuaScripts.Any(l => l.Equals(plotName, StringComparison.OrdinalIgnoreCase)))
            return;

        var activated = model.Threads
            .SelectMany(t => t.Events)
            .Any(e => string.Equals(e.RewardType, "STORY_ELEMENT", StringComparison.OrdinalIgnoreCase)
                      && e.RewardParams.Any(p => p.Position == 0 &&
                                                 (p.RawValue.Equals(plotName, StringComparison.OrdinalIgnoreCase)
                                                  || p.RawValue.Equals(fileName, StringComparison.OrdinalIgnoreCase))));
        if (activated) return;

        diagnostics.Add(new StoryGraphDiagnostic(0, 0, 0, 1,
            $"This plot is suspended in campaign '{model.CampaignName}' and no STORY_ELEMENT reward activates it.",
            XmlDiagnosticSeverity.Information));
    }

    private static void AddTagOrderViolations(List<StoryGraphDiagnostic> diagnostics, StoryThread thread)
    {
        foreach (var storyEvent in thread.Events)
        {
            var maxRank = -1;
            var maxTag = string.Empty;
            foreach (var tag in storyEvent.Tags)
            {
                var rank = RankOf(tag.Name);
                if (rank is null) continue;
                if (rank < maxRank)
                    diagnostics.Add(At(tag.ValueRange,
                        $"'{tag.Name}' appears after '{maxTag}' — the engine expects the documented event tag order.",
                        XmlDiagnosticSeverity.Warning));
                else
                {
                    maxRank = rank.Value;
                    maxTag = tag.Name;
                }
            }
        }
    }

    private void AddOverlongFlagNames(List<StoryGraphDiagnostic> diagnostics, StoryThread thread)
    {
        var eventRefTypes = StoryParamReferenceTypes.Build(schema.GetEnum("StoryEventType"));
        var rewardRefTypes = StoryParamReferenceTypes.Build(schema.GetEnum("StoryRewardType"));

        foreach (var storyEvent in thread.Events)
        {
            Check(storyEvent.EventType, storyEvent.EventParams, eventRefTypes);
            Check(storyEvent.RewardType, storyEvent.RewardParams, rewardRefTypes);
        }

        return;

        void Check(string? typeName, IReadOnlyList<StoryParamSlot> slots,
            Dictionary<(string, int), string> refTypes)
        {
            if (typeName is null) return;
            foreach (var slot in slots)
            {
                if (!refTypes.TryGetValue((typeName.ToUpperInvariant(), slot.Position), out var refType)
                    || refType != StoryParamReferenceTypes.Flag)
                    continue;
                foreach (var flag in StoryParamReferenceTypes.SplitList(slot.RawValue))
                    if (flag.Length > MaxFlagNameLength)
                        diagnostics.Add(At(slot.Range,
                            $"Flag name '{flag}' is {flag.Length} characters long — the engine truncates at {MaxFlagNameLength}.",
                            XmlDiagnosticSeverity.Error));
            }
        }
    }

    private static int? RankOf(string tagName)
    {
        var upper = tagName.ToUpperInvariant();
        for (var i = 0; i < CanonicalOrder.Length; i++)
        {
            var entry = CanonicalOrder[i];
            var matches = entry is "EVENT_PARAM" or "REWARD_PARAM"
                ? upper.StartsWith(entry, StringComparison.Ordinal)
                : upper == entry;
            if (matches) return i;
        }

        return null;
    }

    private static StoryGraphDiagnostic At(StorySourceRange range, string message, XmlDiagnosticSeverity severity)
    {
        return new StoryGraphDiagnostic(range.StartLine, range.StartColumn, range.EndLine, range.EndColumn,
            message, severity);
    }
}
