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
    }
}