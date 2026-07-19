// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Tests.Graph;

public sealed class StoryEvaluatorTest
{
    private const string UriA = "file:///ws/data/xml/story_a.xml";
    private const string UriB = "file:///ws/data/xml/story_b.xml";

    private static StoryGraph BuildGraph(params (string Uri, string Inner)[] threads)
    {
        var parsed = threads
            .Select(t => StoryThreadParser.Parse($"<Story>{t.Inner}</Story>", t.Uri))
            .ToList();
        return new StoryGraphBuilder(new EmptyEnumSchemaProvider()).Build(parsed);
    }

    private static string Id(string uri, string name)
    {
        return $"{uri}#{name.ToLowerInvariant()}";
    }

    private static StoryEventLifecycle LifecycleOf(StoryGraph graph, string uri, string name,
        StoryRuntimeState? state = null)
    {
        var evaluator = new StoryEvaluator(graph);
        return evaluator.GetLifecycle(Id(uri, name), state ?? StoryRuntimeState.Initial);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    [Fact]
    public void NoPrereqs_IsArmed()
    {
        var graph = BuildGraph((UriA, "<Event Name=\"A\"/>"));

        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(graph, UriA, "A"));
    }

    [Fact]
    public void UnsatisfiedPrereq_IsWaiting()
    {
        var graph = BuildGraph((UriA, "<Event Name=\"A\"/><Event Name=\"B\"><Prereq>A</Prereq></Event>"));

        Assert.Equal(StoryEventLifecycle.Waiting, LifecycleOf(graph, UriA, "B"));
    }

    [Fact]
    public void SatisfiedPrereq_IsArmed()
    {
        var graph = BuildGraph((UriA, "<Event Name=\"A\"/><Event Name=\"B\"><Prereq>A</Prereq></Event>"));
        var state = StoryRuntimeState.Initial.WithFired(Id(UriA, "A"));

        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(graph, UriA, "B", state));
    }

    [Theory]
    // OR of AND-lines: <Prereq>A B</Prereq><Prereq>D</Prereq> arms when (A AND B) OR D.
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    public void OrOfAndGroups_TruthTable(bool aFired, bool bFired, bool dFired, bool expectArmed)
    {
        var graph = BuildGraph((UriA,
            "<Event Name=\"A\"/><Event Name=\"B\"/><Event Name=\"D\"/>" +
            "<Event Name=\"C\"><Prereq>A B</Prereq><Prereq>D</Prereq></Event>"));
        var state = StoryRuntimeState.Initial;
        if (aFired) state = state.WithFired(Id(UriA, "A"));
        if (bFired) state = state.WithFired(Id(UriA, "B"));
        if (dFired) state = state.WithFired(Id(UriA, "D"));

        Assert.Equal(expectArmed ? StoryEventLifecycle.Armed : StoryEventLifecycle.Waiting,
            LifecycleOf(graph, UriA, "C", state));
    }

    [Fact]
    public void FiredEvent_IsFired()
    {
        var graph = BuildGraph((UriA, "<Event Name=\"A\"/>"));
        var state = StoryRuntimeState.Initial.WithFired(Id(UriA, "A"));

        Assert.Equal(StoryEventLifecycle.Fired, LifecycleOf(graph, UriA, "A", state));
    }

    [Fact]
    public void FiredPerpetualEvent_RearmsInsteadOfStayingFired()
    {
        var graph = BuildGraph((UriA, "<Event Name=\"A\"><Perpetual>true</Perpetual></Event>"));
        var state = StoryRuntimeState.Initial.WithFired(Id(UriA, "A"));

        Assert.Equal(StoryEventLifecycle.Armed, LifecycleOf(graph, UriA, "A", state));
    }

    [Fact]
    public void DisabledEvent_IsDisabled_RegardlessOfPrereqs()
    {
        var graph = BuildGraph((UriA, "<Event Name=\"A\"/>"));
        var state = StoryRuntimeState.Initial.WithDisabled(Id(UriA, "A"));

        Assert.Equal(StoryEventLifecycle.Disabled, LifecycleOf(graph, UriA, "A", state));
    }

    [Fact]
    public void EventInSuspendedThread_IsInactive()
    {
        var graph = BuildGraph((UriA, "<Event Name=\"A\"/>"));
        var state = StoryRuntimeState.Initial.WithSuspendedThread(UriA);

        Assert.Equal(StoryEventLifecycle.Inactive, LifecycleOf(graph, UriA, "A", state));
    }

    // ── Reachability ─────────────────────────────────────────────────────────

    [Fact]
    public void Reachability_PrereqChainFromRoot_IsReachable()
    {
        var graph = BuildGraph((UriA,
            "<Event Name=\"A\"/><Event Name=\"B\"><Prereq>A</Prereq></Event>" +
            "<Event Name=\"C\"><Prereq>B</Prereq></Event>"));

        var reachable = new StoryEvaluator(graph).ComputeReachableEvents();

        Assert.Contains(Id(UriA, "C"), reachable);
    }

    [Fact]
    public void Reachability_DanglingPrereq_IsUnreachable()
    {
        var graph = BuildGraph((UriA, "<Event Name=\"B\"><Prereq>Ghost</Prereq></Event>"));

        var reachable = new StoryEvaluator(graph).ComputeReachableEvents();

        Assert.DoesNotContain(Id(UriA, "B"), reachable);
    }

    [Fact]
    public void Reachability_PrereqCycle_IsUnreachableWithoutExternalTrigger()
    {
        var graph = BuildGraph((UriA,
            "<Event Name=\"A\"><Prereq>B</Prereq></Event>" +
            "<Event Name=\"B\"><Prereq>A</Prereq></Event>"));

        var reachable = new StoryEvaluator(graph).ComputeReachableEvents();

        Assert.DoesNotContain(Id(UriA, "A"), reachable);
        Assert.DoesNotContain(Id(UriA, "B"), reachable);
    }

    [Fact]
    public void Reachability_ControlEdgeFromReachableEvent_RescuesTarget()
    {
        // Locked's prereq dangles, but a reachable TRIGGER_EVENT forces it - so it IS reachable.
        var graph = BuildGraph(
            (UriA,
                "<Event Name=\"Root\"><Reward_Type>TRIGGER_EVENT</Reward_Type>" +
                "<Reward_Param1>Locked</Reward_Param1></Event>" +
                "<Event Name=\"Locked\"><Prereq>Ghost</Prereq></Event>"));

        var reachable = new StoryEvaluator(graph).ComputeReachableEvents();

        Assert.Contains(Id(UriA, "Locked"), reachable);
    }

    [Fact]
    public void Reachability_CrossThreadPortalTarget_IsReachable()
    {
        var graph = BuildGraph(
            (UriA,
                "<Event Name=\"Root\"><Reward_Type>TRIGGER_EVENT</Reward_Type>" +
                "<Reward_Param1>Remote</Reward_Param1></Event>"),
            (UriB, "<Event Name=\"Remote\"><Prereq>Ghost</Prereq></Event>"));

        var reachable = new StoryEvaluator(graph).ComputeReachableEvents();

        Assert.Contains(Id(UriB, "Remote"), reachable);
    }

    // ── Prereq cycles ────────────────────────────────────────────────────────

    [Fact]
    public void PrereqCycles_TwoEventCycle_IsDetected()
    {
        var graph = BuildGraph((UriA,
            "<Event Name=\"A\"><Prereq>B</Prereq></Event>" +
            "<Event Name=\"B\"><Prereq>A</Prereq></Event>"));

        var cycle = Assert.Single(new StoryEvaluator(graph).FindPrereqCycles());
        Assert.Equal([Id(UriA, "A"), Id(UriA, "B")], cycle.Order());
    }

    [Fact]
    public void PrereqCycles_AcyclicChain_YieldsNothing()
    {
        var graph = BuildGraph((UriA,
            "<Event Name=\"A\"/><Event Name=\"B\"><Prereq>A</Prereq></Event>"));

        Assert.Empty(new StoryEvaluator(graph).FindPrereqCycles());
    }
}

file sealed class EmptyEnumSchemaProvider : ISchemaProvider
{
    private static readonly EnumDefinition Rewards = new()
    {
        Name = "StoryRewardType",
        Values =
        [
            new EnumValueDefinition
            {
                Name = "TRIGGER_EVENT",
                Params =
                [
                    new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = "StoryEventName"
                    }
                ]
            }
        ]
    };

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [Rewards];
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
        return string.Equals(name, Rewards.Name, StringComparison.OrdinalIgnoreCase) ? Rewards : null;
    }

    public GameObjectTypeDefinition? GetObjectType(string t)
    {
        return null;
    }
}