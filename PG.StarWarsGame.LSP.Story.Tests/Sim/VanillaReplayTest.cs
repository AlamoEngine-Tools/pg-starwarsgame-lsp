// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Story.Tests.Sim;

/// <summary>
///     The "no dead ends" property over the shipped campaigns: drive a vanilla story to exhaustion
///     with a scripted answer policy and assert that every event left Armed is one the simulator
///     can still fire on its own or one it offers as an intervention. A simulator rule that leaves
///     an event silently stuck (the STORY_TRIGGER bug this chunk fixes) fails here. Skipped when
///     the game corpus or the schema is not checked out next to the repo.
/// </summary>
public sealed class VanillaReplayTest(ITestOutputHelper output)
{
    private const int MaxCommands = 20000;
    private const int IdleAdvancesBeforeStop = 60;
    private const double ClockStepSeconds = 10;

    private static readonly HashSet<string> SelfFiringTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "STORY_ELAPSED", "STORY_FLAG", "STORY_TRIGGER"
    };

    public static bool CorpusPresent => FindRepoRoot() is not null;

    [Theory(SkipUnless = nameof(CorpusPresent),
        Skip = "Game corpus (eaw/, foc/) or schema/eaw not found next to the repo.")]
    [InlineData("foc", "Full_Story_Campaign_Underworld", "Underworld")]
    // Vanilla's CampaignFiles.xml comments Campaigns_Main.xml out; the shipped story campaigns
    // are the Full_Story_* pair in Campaigns_Alpha.xml.
    [InlineData("eaw", "Full_Story_Campaign_Rebel", "Rebel")]
    [InlineData("eaw", "Full_Story_Campaign_Empire", "Empire")]
    public void Replay_LeavesNoArmedEventWithoutAWayToFire(string game, string campaign, string faction)
    {
        var root = FindRepoRoot()!;
        using var schema = new LocalFileSchemaProvider(Path.Combine(root, "schema", "eaw"), new FileSystem(),
            NullLogger<LocalFileSchemaProvider>.Instance);
        var resolver = new DiskResolver(Path.Combine(root, game, "Data", "XML"));
        var chain = new StoryChainScanner(resolver).Scan("CampaignFiles.xml");
        // The campaign's scripts as machines, from the shipped Scripts/Story folder, so the replay
        // measures how much of the Lua overlay the vanilla scripts light up.
        var scriptsDir = Path.Combine(root, game, "Data", "Scripts", "Story");
        var scriptFiles = Directory.Exists(scriptsDir)
            ? Directory.EnumerateFiles(scriptsDir, "*.lua").ToDictionary(Path.GetFileNameWithoutExtension,
                p => p, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var model = new StoryCampaignAssembler(schema).Assemble(campaign, faction, chain, rel =>
        {
            var file = resolver.ReadFile(rel);
            return file is null ? null : (file.DocumentUri!, file.Content);
        }, name => scriptFiles.TryGetValue(name, out var path)
            ? LuaStoryMachineExtractor.Extract(File.ReadAllText(path), new Uri(path).AbsoluteUri.ToLowerInvariant())
            : null);
        Assert.True(model is not null,
            $"{campaign}/{faction} did not assemble. Campaigns found: " + string.Join("; ",
                chain.Campaigns.Select(c =>
                    c.Name + " [" +
                    string.Join(", ", c.FactionManifests.Select(f => f.Faction + "=" + f.ManifestFile)) + "]")));

        // One replay per scope, as the game runs them: the galactic story on its own clock, and
        // each battle from its own tick 0. A battle's listeners never arm in the galaxy.
        var scopes = new List<string?> { null };
        scopes.AddRange(model.Battles.Select(b => b.Key));
        foreach (var scope in scopes) ReplayScope(scope);
        return;

        void ReplayScope(string? scope)
        {
            var sim = new StorySimulator(model, schema, null, scope);
            var snapshot = sim.Start();
            var commands = 0;
            var idleAdvances = 0;
            // A perpetual manual event re-arms after every answer; answering it forever would be the
            // policy looping, not the story. Eight answers per node, then it counts as settled.
            var answers = new Dictionary<string, int>(StringComparer.Ordinal);
            while (commands < MaxCommands)
            {
                commands++;
                // The fight was chosen (Underworld forces the button itself): the galaxy is frozen
                // until the battle is played to its end. The policy plays it won, as the author
                // does in the battle's own panel; the battle's own story replays as its own scope.
                if (snapshot.Runtime.World is
                    { PendingBattle: { } fighting, PendingBattleChoice: StoryBattleChoice.Fight })
                {
                    snapshot = sim.ResolveBattleOutcome(snapshot,
                        new StoryWorldChange(StoryWorldChangeKind.BattleWon) { BattleKey = fighting });
                    continue;
                }

                // A notification a script already owes is the script's to deliver: the policy leaves
                // it alone and lets the clock bring it, so the overlay is measured, not pre-empted.
                var owed = snapshot.Runtime.PendingEmissions.Select(p => p.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var next = sim.GetInterventions(snapshot).FirstOrDefault(i =>
                    answers.GetValueOrDefault(i.NodeId) < 8 &&
                    !(i.Kind == "lua" && i.Options.Any(owed.Contains)));
                if (next is not null)
                {
                    idleAdvances = 0;
                    answers[next.NodeId] = answers.GetValueOrDefault(next.NodeId) + 1;
                    // The policy answers the way an author would: change the world the event asks
                    // for when it names one, else the Lua notification, else assume the trigger met.
                    snapshot = next switch
                    {
                        { Kind: "lua", Options.Count: > 0 } => sim.LuaNotify(snapshot, next.Options[0]),
                        // The pending battle: the author decides it won on the portal, as the
                        // service does when nothing inside the battle is played.
                        { Kind: StorySimIntervention.BattleKind, BattleKey: { } pendingKey } =>
                            sim.ResolveBattleOutcome(snapshot,
                                new StoryWorldChange(StoryWorldChangeKind.BattleWon) { BattleKey = pendingKey }),
                        // A battle's outcome is its resolution, as the service applies it: the
                        // pending battle ends, the summary closes and the galaxy runs on.
                        { Kind: "tactical", Suggested: { } outcome, BattleKey: { } battleKey } =>
                            sim.ResolveBattleOutcome(snapshot, outcome with { BattleKey = battleKey }),
                        { Suggested: { } change } => sim.ApplyWorldChange(snapshot, change),
                        _ => sim.SatisfyTrigger(snapshot, next.NodeId)
                    };
                    continue;
                }

                var before = Fingerprint(sim, snapshot);
                snapshot = sim.AdvanceClock(snapshot, ClockStepSeconds);
                idleAdvances = Fingerprint(sim, snapshot) == before ? idleAdvances + 1 : 0;
                if (idleAdvances >= IdleAdvancesBeforeStop) break;
            }

            var lifecycles = sim.GetLifecycles(snapshot);
            var offered = sim.GetInterventions(snapshot).Select(i => i.NodeId).ToHashSet(StringComparer.Ordinal);
            offered.UnionWith(answers.Where(kvp => kvp.Value >= 8).Select(kvp => kvp.Key));
            // The listener for a battle's other outcome stays armed once the galaxy has taken the
            // result - the game never takes that route, so it is moot, not stuck.
            var outcomes = snapshot.Runtime.World.BattleOutcomes;
            // A generic listener the game never raises is dead by measurement - the graph reports
            // it - not stuck.
            var neverRaised = model.Graph.Problems
                .Where(p => p.Kind == StoryGraphProblemKind.GenericNeverRaised)
                .Select(p => (p.DocumentUri, p.Range.StartLine))
                .ToHashSet();
            var stuck = lifecycles
                .Where(kvp => kvp.Value == StoryEventLifecycle.Armed && !offered.Contains(kvp.Key))
                .Where(kvp => sim.BattleOf(kvp.Key) is not { } battle || !outcomes.ContainsKey(battle))
                .Select(kvp => model.Graph.Nodes.First(n => n.Id == kvp.Key))
                .Where(n => !SelfFiringTypes.Contains(n.Event!.EventType ?? ""))
                .Where(n => !n.Event!.EventParams.Any(p =>
                    n.ThreadUri is { } uri && neverRaised.Contains((uri, p.Range.StartLine))))
                .Select(n => $"{n.Event!.Name} ({n.Event.EventType})")
                .ToList();

            var fired = lifecycles.Values.Count(l => l == StoryEventLifecycle.Fired);
            output.WriteLine($"{game}/{campaign}/{faction} [{scope ?? "galactic"}]: events {lifecycles.Count}, " +
                             $"fired {fired}, commands {commands}, clock {snapshot.Clock:0}s, log lines {snapshot.Log.Count}");
            // Events the policy answered three times without firing: a facet whose suggested change
            // does not satisfy the event's own parameters. Not a dead end, but a modelling gap to read.
            var everFired = snapshot.Steps.Where(s => s.To == StoryEventLifecycle.Fired).Select(s => s.NodeId)
                .ToHashSet(StringComparer.Ordinal);
            var unanswered = answers.Where(kvp => kvp.Value >= 8 && !everFired.Contains(kvp.Key))
                .Select(kvp => model.Graph.Nodes.First(n => n.Id == kvp.Key).Event!)
                .Select(e => $"{e.Name} ({e.EventType})")
                .ToList();
            output.WriteLine($"  answered 8x without firing: {unanswered.Count}" +
                             (unanswered.Count > 0 ? " - " + string.Join(", ", unanswered.Take(12)) : ""));
            var states = model.LuaMachines.Sum(m => m.States.Count);
            var emissions = model.LuaMachines.Sum(m =>
                m.States.Sum(s => s.OnEnter.Emissions.Count + s.OnUpdate.Emissions.Count + s.OnExit.Emissions.Count));
            var luaLinks = model.Graph.Edges.Count(e => e.Kind == StoryEdgeKind.LuaLink);
            var entered = snapshot.Steps.Count(s => s.Cause == StorySimCause.LuaEnter);
            // Only fires whose source is a script state came from an owed emission; the policy's own
            // Story_Event answers carry no source.
            var luaFired = snapshot.Steps.Count(s =>
                s.Cause == StorySimCause.Lua && s.To == StoryEventLifecycle.Fired &&
                s.SourceNodeId?.Contains("#lua#", StringComparison.Ordinal) == true);
            var unheard = snapshot.Steps.Count(s =>
                s.Cause == StorySimCause.Ignored && s.Detail!.Contains("no armed event listens"));
            var dropped = snapshot.Steps.Count(s => s.Cause == StorySimCause.Ignored && s.Detail!.Contains("dropped"));
            output.WriteLine($"  lua: {model.LuaMachines.Count} scripts, {states} states, {emissions} emissions, " +
                             $"{luaLinks} links; entered {entered} states, {luaFired} notifications fired from scripts, " +
                             $"{unheard} emitted with no armed listener, {snapshot.Runtime.PendingEmissions.Count} still owed, " +
                             $"{dropped} triggers dropped while a transition was pending");

            Assert.True(commands < MaxCommands,
                $"The {scope ?? "galactic"} replay did not settle within the command budget.");
            if (stuck.Count > 0)
            {
                // The battle the galaxy is waiting on, if any, and how the log got there.
                output.WriteLine($"  pending battle: {snapshot.Runtime.World.PendingBattle ?? "none"} " +
                                 $"({snapshot.Runtime.World.PendingBattleChoice ?? "no choice"})");
                foreach (var line in snapshot.Log.Where(l =>
                             l.Contains("battle", StringComparison.OrdinalIgnoreCase) ||
                             l.Contains("LINK_TACTICAL", StringComparison.Ordinal)).TakeLast(20))
                    output.WriteLine("    " + line);
            }

            Assert.Empty(stuck);
        }
    }

    private static string Fingerprint(StorySimulator sim, StorySimSnapshot snapshot)
    {
        return string.Join(",", sim.GetLifecycles(snapshot).OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => kvp.Key + "=" + kvp.Value)) + "|" + snapshot.Runtime.Flags.Count;
    }

    /// <summary>The repo root holding the gitignored eaw/ and foc/ corpora; null on a clean clone.</summary>
    internal static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(VanillaReplayTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "foc", "Data", "XML", "CampaignFiles.xml"))
                && File.Exists(Path.Combine(dir.FullName, "eaw", "Data", "XML", "CampaignFiles.xml"))
                && Directory.Exists(Path.Combine(dir.FullName, "schema", "eaw")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>Case-insensitive lookup over one game's Data/XML tree, the way the engine resolves names.</summary>
    internal sealed class DiskResolver : IStoryChainFileResolver
    {
        private readonly Dictionary<string, string> _files;

        public DiskResolver(string xmlRoot)
        {
            _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(xmlRoot, "*.xml", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(xmlRoot, path).Replace('\\', '/');
                _files.TryAdd(rel, path);
            }
        }

        public StoryChainFile? ReadFile(string xmlRelativePath)
        {
            return _files.TryGetValue(xmlRelativePath.Replace('\\', '/'), out var path)
                ? new StoryChainFile(File.ReadAllText(path), new Uri(path).AbsoluteUri.ToLowerInvariant())
                : null;
        }

        public bool IsKnownToBaseline(string xmlRelativePath)
        {
            return false;
        }
    }
}