// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Tests.Sim;

namespace PG.StarWarsGame.LSP.Story.Tests.Graph;

/// <summary>
///     The builder's name scoping measured against the shipped campaigns: prerequisites and
///     subplot-local rewards resolve inside their own thread file as the engine does, so a name
///     that also exists in another file is never an ambiguity and never a cross-file edge. The
///     shipped data runs clean in the game, so the only findings left are the handful of
///     prerequisites the engine itself asserts on.
/// </summary>
public sealed class VanillaGraphScopeTest(ITestOutputHelper output)
{
    public static bool CorpusPresent => VanillaReplayTest.FindRepoRoot() is not null;

    [Theory(SkipUnless = nameof(CorpusPresent),
        Skip = "Game corpus (eaw/, foc/) or schema/eaw not found next to the repo.")]
    [InlineData("foc", "Full_Story_Campaign_Underworld", "Underworld")]
    [InlineData("eaw", "Full_Story_Campaign_Rebel", "Rebel")]
    [InlineData("eaw", "Full_Story_Campaign_Empire", "Empire")]
    public void Vanilla_HasNoAmbiguityAndNoCrossFilePrereqEdge(string game, string campaign, string faction)
    {
        var root = VanillaReplayTest.FindRepoRoot()!;
        using var schema = new LocalFileSchemaProvider(Path.Combine(root, "schema", "eaw"), new FileSystem(),
            NullLogger<LocalFileSchemaProvider>.Instance);
        var resolver = new VanillaReplayTest.DiskResolver(Path.Combine(root, game, "Data", "XML"));
        var chain = new StoryChainScanner(resolver).Scan("CampaignFiles.xml");
        var model = new StoryCampaignAssembler(schema).Assemble(campaign, faction, chain, rel =>
        {
            var file = resolver.ReadFile(rel);
            return file is null ? null : (file.DocumentUri!, file.Content);
        });
        Assert.NotNull(model);

        var graph = model.Graph;
        var threadOf = graph.Nodes.Where(n => n.Kind == StoryNodeKind.Event)
            .ToDictionary(n => n.Id, n => n.ThreadUri, StringComparer.Ordinal);
        var junctionThread = graph.Nodes.Where(n => n.Kind is StoryNodeKind.AndJunction or StoryNodeKind.OrJunction)
            .ToDictionary(n => n.Id, n => n.ThreadUri, StringComparer.Ordinal);
        string? ThreadOf(string id) => threadOf.GetValueOrDefault(id) ?? junctionThread.GetValueOrDefault(id);

        var crossFilePrereqs = graph.Edges
            .Where(e => e.Kind == StoryEdgeKind.Prereq)
            .Where(e => ThreadOf(e.FromId) is { } from && ThreadOf(e.ToId) is { } to
                                                       && !string.Equals(from, to, StringComparison.Ordinal))
            .ToList();
        var byKind = graph.Problems.GroupBy(p => p.Kind).ToDictionary(g => g.Key, g => g.Count());

        output.WriteLine($"{game}/{campaign}: events {threadOf.Count}, prereq edges " +
                         $"{graph.Edges.Count(e => e.Kind == StoryEdgeKind.Prereq)}, cross-file prereq edges " +
                         $"{crossFilePrereqs.Count}, problems " +
                         string.Join(", ", byKind.Select(kv => $"{kv.Key}={kv.Value}")));
        foreach (var problem in graph.Problems)
            output.WriteLine($"  {problem.Kind}: {problem.Message}");

        Assert.Empty(crossFilePrereqs);
        Assert.DoesNotContain(graph.Problems, p => p.Kind == StoryGraphProblemKind.AmbiguousTarget);

        // Battles as sub-graphs: the galactic scope plus every battle scope carries each event
        // exactly once, the galactic scope shows one portal per battle and nothing of theirs, and
        // every battle scope has its entry portal.
        var galactic = StoryGraphScoper.Scope(model, null);
        var battles = model.Battles;
        var galacticEvents = galactic.Nodes.Count(n => n.Kind == StoryNodeKind.Event);
        var battleEvents = battles.Sum(b =>
            StoryGraphScoper.Scope(model, b.Key).Nodes.Count(n => n.Kind == StoryNodeKind.Event));
        output.WriteLine(
            $"  galactic events {galacticEvents} in {galactic.Nodes.Where(n => n.Kind == StoryNodeKind.Event).Select(n => n.ThreadUri).Distinct().Count()} threads, " +
            $"battles {battles.Count} with {battleEvents} events: " +
            string.Join(", ", battles.OrderBy(b => b.Rank).Select(b => $"{b.Rank}:{b.Label}")));
        Assert.Equal(threadOf.Count, galacticEvents + battleEvents);

        // The implicit edges, counted per rule over the whole campaign and the galactic scope:
        // the corpus numbers the rule table carries.
        var implicitByLabel = graph.Edges.Where(e => e.Kind == StoryEdgeKind.Implicit)
            .GroupBy(e => e.Label ?? "").OrderBy(g => g.Key)
            .Select(g => $"{g.Key}={g.Count()}");
        var galacticImplicit = galactic.Edges.Where(e => e.Kind == StoryEdgeKind.Implicit)
            .GroupBy(e => e.Label ?? "").OrderBy(g => g.Key)
            .Select(g => $"{g.Key}={g.Count()}");
        output.WriteLine($"  implicit edges: campaign {string.Join(", ", implicitByLabel)}; galactic scope " +
                         string.Join(", ", galacticImplicit));
        Assert.Equal(battles.Count, galactic.Nodes.Count(n => n.Kind == StoryNodeKind.TacticalPlot));
        Assert.DoesNotContain(galactic.Nodes, n => n.ThreadUri is { } t && battles.Any(b => b.ThreadUris.Contains(t)));
        foreach (var battle in battles)
        {
            var scoped = StoryGraphScoper.Scope(model, battle.Key);
            Assert.Contains(scoped.Nodes,
                n => n.Kind == StoryNodeKind.GalacticPortal && battle.EntryEventIds.Contains(n.PortalTarget!));
            Assert.DoesNotContain(scoped.Nodes, n => n.Kind == StoryNodeKind.TacticalPlot);
        }
    }
}