// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Story.Tests.Sim;

/// <summary>Scenario tests: scripted command sequences asserting lifecycle progressions.</summary>
public sealed class StorySimulatorTest
{
    private const string ThreadAUri = "file:///ws/data/xml/story_a.xml";
    private const string ThreadBUri = "file:///ws/data/xml/story_b.xml";

    private const string ThreadAText =
        "<Story>\n" +
        "\t<Event Name=\"Begin\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>0</Event_Param1>\n" +
        "\t\t<Reward_Type>TRIGGER_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Chained</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Chained\">\n" +
        "\t\t<Event_Type>STORY_TRIGGER</Event_Type>\n" +
        "\t\t<Prereq>Begin</Prereq>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Later\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>10</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"FlagWatcher\">\n" +
        "\t\t<Event_Type>STORY_FLAGS</Event_Type>\n" +
        "\t\t<Event_Param1>FLAG_X</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Setter\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>SET_FLAG</Reward_Type>\n" +
        "\t\t<Reward_Param1>FLAG_Y</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Notify\">\n" +
        "\t\t<Event_Type>STORY_AI_NOTIFICATION</Event_Type>\n" +
        "\t\t<Event_Param1>Alert_One</Event_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Victim\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Disabler\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>DISABLE_STORY_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Victim</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Activator\">\n" +
        "\t\t<Event_Type>STORY_GENERIC</Event_Type>\n" +
        "\t\t<Reward_Type>STORY_ELEMENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>story_b</Reward_Param1>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"PerpFlag\">\n" +
        "\t\t<Event_Type>STORY_FLAGS</Event_Type>\n" +
        "\t\t<Event_Param1>FLAG_P</Event_Param1>\n" +
        "\t\t<Perpetual>Yes</Perpetual>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    private const string ThreadBText =
        "<Story>\n" +
        "\t<Event Name=\"BEvent\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>0</Event_Param1>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    private static (StorySimulator Sim, StoryCampaignModel Model) Build()
    {
        var schema = new SimSchemaProvider();
        var threadA = StoryThreadParser.Parse(ThreadAText, ThreadAUri);
        var threadB = StoryThreadParser.Parse(ThreadBText, ThreadBUri);
        var model = new StoryCampaignModel("GC", [threadA, threadB],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ThreadBUri },
            new StoryGraphBuilder(schema).Build([threadA, threadB]));
        return (new StorySimulator(model, schema), model);
    }

    private static string NodeId(StoryCampaignModel model, string eventName)
    {
        return model.Graph.Nodes.Single(n =>
            n.Kind == StoryNodeKind.Event && n.Event!.Name == eventName).Id;
    }

    private static StoryEventLifecycle LifecycleOf(
        StorySimulator sim, StorySimSnapshot snapshot, StoryCampaignModel model, string eventName)
    {
        return sim.GetLifecycles(snapshot)[NodeId(model, eventName)];
    }

    [Fact]
    public void Start_AutoFiresElapsedZero_AndCascadesControlEdges()
    {
        var (sim, model) = Build();

        var snapshot = sim.Start();

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Begin"));
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Chained"));
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Later"));
        Assert.Equal(StoryEventLifecycle.Inactive, LifecycleOf(sim, snapshot, model, "BEvent"));
    }

    [Fact]
    public void AdvanceClock_FiresElapsedEventsAtTheirTime()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.AdvanceClock(snapshot, 9);
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "Later"));

        snapshot = sim.AdvanceClock(snapshot, 1);
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Later"));
    }

    [Fact]
    public void SetFlag_FiresFlagWatchers()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.SetFlag(snapshot, "FLAG_X", 1);

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "FlagWatcher"));
    }

    [Fact]
    public void SatisfyTrigger_FiresArmedEvent_AndAppliesFlagRewards()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Setter"));

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Setter"));
        Assert.Equal(1, snapshot.Runtime.Flags["FLAG_Y"]);
    }

    [Fact]
    public void SatisfyTrigger_NotArmed_IsIgnoredWithWarning()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        var next = sim.SatisfyTrigger(snapshot, NodeId(model, "Chained"));

        Assert.Equal(snapshot.Runtime.FiredEvents, next.Runtime.FiredEvents);
        Assert.Contains(next.Log, l => l.Contains("not armed"));
    }

    [Fact]
    public void LuaNotify_FiresMatchingAiNotification()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.LuaNotify(snapshot, "Alert_One");
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "Notify"));

        var unmatched = sim.LuaNotify(snapshot, "Alert_Ghost");
        Assert.Contains(unmatched.Log, l => l.Contains("No armed event listens"));
    }

    [Fact]
    public void DisableReward_DisablesTheTarget()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Disabler"));

        Assert.Equal(StoryEventLifecycle.Disabled, LifecycleOf(sim, snapshot, model, "Victim"));
    }

    [Fact]
    public void StoryElementReward_ActivatesTheSuspendedThread()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();
        Assert.Equal(StoryEventLifecycle.Inactive, LifecycleOf(sim, snapshot, model, "BEvent"));

        snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Activator"));

        // Thread B is active now; its elapsed-0 event fires in the same cascade.
        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(sim, snapshot, model, "BEvent"));
    }

    [Fact]
    public void PerpetualEvent_RefiresOncePerCommandCascade()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        snapshot = sim.SetFlag(snapshot, "FLAG_P", 1);
        var firstCount = snapshot.Log.Count(l => l.Contains("Fired 'PerpFlag'"));

        snapshot = sim.AdvanceClock(snapshot, 1);
        var secondCount = snapshot.Log.Count(l => l.Contains("Fired 'PerpFlag'"));

        Assert.Equal(1, firstCount);
        Assert.Equal(2, secondCount);
        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(sim, snapshot, model, "PerpFlag"));
    }

    [Fact]
    public void Interventions_ListArmedManualEvents_WithKinds()
    {
        var (sim, model) = Build();
        var snapshot = sim.Start();

        var interventions = sim.GetInterventions(snapshot);

        Assert.DoesNotContain(interventions, i => i.EventName is "Begin" or "Chained" or "Later" or "FlagWatcher");
        Assert.Equal("lua", Assert.Single(interventions, i => i.EventName == "Notify").Kind);
        Assert.Equal(["Alert_One"], Assert.Single(interventions, i => i.EventName == "Notify").Options);
        Assert.Equal("manual", Assert.Single(interventions, i => i.EventName == "Setter").Kind);
        _ = NodeId(model, "Setter"); // fixture sanity
    }

    [Fact]
    public void SameCommandSequence_ProducesIdenticalState()
    {
        static string Fingerprint(StorySimulator sim, StorySimSnapshot snapshot)
        {
            var fired = string.Join(",", snapshot.Runtime.FiredEvents.OrderBy(x => x, StringComparer.Ordinal));
            var flags = string.Join(",", snapshot.Runtime.Flags
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
            return $"{fired}|{flags}|{snapshot.Clock}|{string.Join(";", snapshot.Log)}";
        }

        static string Run()
        {
            var (sim, model) = Build();
            var snapshot = sim.Start();
            snapshot = sim.SetFlag(snapshot, "FLAG_X", 1);
            snapshot = sim.AdvanceClock(snapshot, 10);
            snapshot = sim.SatisfyTrigger(snapshot, NodeId(model, "Setter"));
            snapshot = sim.LuaNotify(snapshot, "Alert_One");
            return Fingerprint(sim, snapshot);
        }

        Assert.Equal(Run(), Run());
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class SimSchemaProvider : ISchemaProvider
    {
        private static readonly EnumDefinition Events = new()
        {
            Name = "StoryEventType",
            Values =
            [
                new EnumValueDefinition { Name = "STORY_ELAPSED" },
                new EnumValueDefinition { Name = "STORY_TRIGGER" },
                new EnumValueDefinition
                {
                    Name = "STORY_FLAGS",
                    Params = [new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = StoryReferenceTypes.Flag
                    }]
                },
                new EnumValueDefinition
                {
                    Name = "STORY_AI_NOTIFICATION",
                    Params = [new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = StoryReferenceTypes.Notification
                    }]
                },
                new EnumValueDefinition { Name = "STORY_GENERIC" }
            ]
        };

        private static readonly EnumDefinition Rewards = new()
        {
            Name = "StoryRewardType",
            Values =
            [
                new EnumValueDefinition
                {
                    Name = "TRIGGER_EVENT",
                    Params = [new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = StoryReferenceTypes.EventName
                    }]
                },
                new EnumValueDefinition
                {
                    Name = "SET_FLAG",
                    Params = [new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = StoryReferenceTypes.Flag
                    }]
                },
                new EnumValueDefinition
                {
                    Name = "DISABLE_STORY_EVENT",
                    Params = [new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = StoryReferenceTypes.EventName
                    }]
                },
                new EnumValueDefinition { Name = "STORY_ELEMENT" }
            ]
        };

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [Events, Rewards];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public XmlTagDefinition? GetTag(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string name)
        {
            if (string.Equals(name, Events.Name, StringComparison.OrdinalIgnoreCase)) return Events;
            if (string.Equals(name, Rewards.Name, StringComparison.OrdinalIgnoreCase)) return Rewards;
            return null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }
    }
}
