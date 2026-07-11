// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Tests.Graph;

public sealed class StoryGraphBuilderTest
{
    private const string UriA = "file:///ws/data/xml/story_a.xml";
    private const string UriB = "file:///ws/data/xml/story_b.xml";

    // ── Fixture: schema slice with the edge-driving referenceTypes ───────────

    private static ISchemaProvider Schema { get; } = new StubSchemaProvider(
        new EnumDefinition
        {
            Name = "StoryEventType",
            Values =
            [
                Value("STORY_TRIGGER"),
                Value("STORY_FLAG", Param(0, "StoryFlag")),
                Value("STORY_LAND_TACTICAL", Param(0, "StoryPlotFile"))
            ]
        },
        new EnumDefinition
        {
            Name = "StoryRewardType",
            Values =
            [
                Value("TRIGGER_EVENT", Param(0, "StoryEventName")),
                Value("SET_FLAG", Param(0, "StoryFlag")),
                Value("DISABLE_BRANCH", Param(0, "StoryBranch"))
            ]
        });

    private static EnumValueDefinition Value(string name, params ParamDefinition[] parameters)
    {
        return new EnumValueDefinition { Name = name, Params = parameters.Length > 0 ? parameters : null };
    }

    private static ParamDefinition Param(int position, string referenceTypeName)
    {
        return new ParamDefinition
        {
            Position = position,
            ValueType = XmlValueType.NameReference,
            ReferenceTypeName = referenceTypeName
        };
    }

    private static StoryThread Thread(string uri, string inner)
    {
        return StoryThreadParser.Parse($"<Story>{inner}</Story>", uri);
    }

    private static StoryGraph Build(params StoryThread[] threads)
    {
        return new StoryGraphBuilder(Schema).Build(threads);
    }

    private static string EventId(string uri, string name)
    {
        return $"{uri}#{name.ToLowerInvariant()}";
    }

    // ── Prereq edges and junctions ───────────────────────────────────────────

    [Fact]
    public void Prereq_SingleGroupSingleToken_DirectEdge()
    {
        var graph = Build(Thread(UriA,
            "<Event Name=\"A\"><Event_Type>STORY_TRIGGER</Event_Type></Event>" +
            "<Event Name=\"B\"><Prereq>A</Prereq></Event>"));

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(StoryEdgeKind.Prereq, edge.Kind);
        Assert.Equal(EventId(UriA, "A"), edge.FromId);
        Assert.Equal(EventId(UriA, "B"), edge.ToId);
        Assert.DoesNotContain(graph.Nodes, n => n.Kind is StoryNodeKind.AndJunction or StoryNodeKind.OrJunction);
    }

    [Fact]
    public void Prereq_SingleGroupMultipleTokens_RoutesThroughAndJunction()
    {
        var graph = Build(Thread(UriA,
            "<Event Name=\"A\"/><Event Name=\"B\"/>" +
            "<Event Name=\"C\"><Prereq>A B</Prereq></Event>"));

        var junction = Assert.Single(graph.Nodes, n => n.Kind == StoryNodeKind.AndJunction);
        Assert.Equal($"{EventId(UriA, "C")}#g0", junction.Id);
        Assert.Contains(graph.Edges, e => e.FromId == EventId(UriA, "A") && e.ToId == junction.Id);
        Assert.Contains(graph.Edges, e => e.FromId == EventId(UriA, "B") && e.ToId == junction.Id);
        Assert.Contains(graph.Edges, e => e.FromId == junction.Id && e.ToId == EventId(UriA, "C"));
    }

    [Fact]
    public void Prereq_MultipleGroups_OrJunctionCollectsGroups()
    {
        var graph = Build(Thread(UriA,
            "<Event Name=\"A\"/><Event Name=\"B\"/><Event Name=\"D\"/>" +
            "<Event Name=\"C\"><Prereq>A B</Prereq><Prereq>D</Prereq></Event>"));

        var orJunction = Assert.Single(graph.Nodes, n => n.Kind == StoryNodeKind.OrJunction);
        var andJunction = Assert.Single(graph.Nodes, n => n.Kind == StoryNodeKind.AndJunction);
        Assert.Equal($"{EventId(UriA, "C")}#or", orJunction.Id);
        // AND-group feeds the OR junction; the single-token group connects directly to it.
        Assert.Contains(graph.Edges, e => e.FromId == andJunction.Id && e.ToId == orJunction.Id);
        Assert.Contains(graph.Edges, e => e.FromId == EventId(UriA, "D") && e.ToId == orJunction.Id);
        Assert.Contains(graph.Edges, e => e.FromId == orJunction.Id && e.ToId == EventId(UriA, "C"));
    }

    [Fact]
    public void Prereq_UnknownEventName_RecordsDanglingProblem()
    {
        var graph = Build(Thread(UriA, "<Event Name=\"B\"><Prereq>Ghost</Prereq></Event>"));

        Assert.Empty(graph.Edges);
        var problem = Assert.Single(graph.Problems);
        Assert.Equal(StoryGraphProblemKind.DanglingPrereq, problem.Kind);
        Assert.Equal("Ghost", problem.Reference);
        Assert.Equal(UriA, problem.DocumentUri);
    }

    [Fact]
    public void Prereq_ResolvesCaseInsensitively()
    {
        var graph = Build(Thread(UriA,
            "<Event Name=\"Alpha\"/><Event Name=\"B\"><Prereq>ALPHA</Prereq></Event>"));

        Assert.Empty(graph.Problems);
        Assert.Single(graph.Edges);
    }

    // ── Control edges ────────────────────────────────────────────────────────

    [Fact]
    public void ControlEdge_TriggerEvent_SameThread_IsDirect()
    {
        var graph = Build(Thread(UriA,
            "<Event Name=\"Target\"/>" +
            "<Event Name=\"Source\"><Reward_Type>TRIGGER_EVENT</Reward_Type>" +
            "<Reward_Param1>Target</Reward_Param1></Event>"));

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(StoryEdgeKind.Control, edge.Kind);
        Assert.Equal("TRIGGER_EVENT", edge.Label);
        Assert.Equal(EventId(UriA, "Source"), edge.FromId);
        Assert.Equal(EventId(UriA, "Target"), edge.ToId);
    }

    [Fact]
    public void ControlEdge_CrossThreadTarget_RoutesThroughPortal()
    {
        var graph = Build(
            Thread(UriA,
                "<Event Name=\"Source\"><Reward_Type>TRIGGER_EVENT</Reward_Type>" +
                "<Reward_Param1>Target</Reward_Param1></Event>"),
            Thread(UriB, "<Event Name=\"Target\"/>"));

        var portal = Assert.Single(graph.Nodes, n => n.Kind == StoryNodeKind.Portal);
        Assert.Equal(UriA, portal.ThreadUri);
        Assert.Contains(graph.Edges,
            e => e.FromId == EventId(UriA, "Source") && e.ToId == portal.Id && e.Kind == StoryEdgeKind.Control);
        Assert.Contains(graph.Edges,
            e => e.FromId == portal.Id && e.ToId == EventId(UriB, "Target") && e.Kind == StoryEdgeKind.Control);
    }

    [Fact]
    public void ControlEdge_AmbiguousCampaignGlobalTarget_EdgesToAllAndRecordsProblem()
    {
        var graph = Build(
            Thread(UriA,
                "<Event Name=\"Twin\"/>" +
                "<Event Name=\"Source\"><Reward_Type>TRIGGER_EVENT</Reward_Type>" +
                "<Reward_Param1>Twin</Reward_Param1></Event>"),
            Thread(UriB, "<Event Name=\"Twin\"/>"));

        Assert.Contains(graph.Problems, p => p.Kind == StoryGraphProblemKind.AmbiguousTarget
                                             && p.Reference == "Twin");
        // Both twins receive an edge (one direct, one through the cross-file portal).
        Assert.Contains(graph.Edges, e => e.ToId == EventId(UriA, "Twin"));
        Assert.Contains(graph.Edges, e => e.ToId == EventId(UriB, "Twin"));
    }

    [Fact]
    public void ControlEdge_UnresolvedTarget_RecordsProblem()
    {
        var graph = Build(Thread(UriA,
            "<Event Name=\"Source\"><Reward_Type>TRIGGER_EVENT</Reward_Type>" +
            "<Reward_Param1>Ghost</Reward_Param1></Event>"));

        var problem = Assert.Single(graph.Problems);
        Assert.Equal(StoryGraphProblemKind.UnresolvedControlTarget, problem.Kind);
        Assert.Equal("Ghost", problem.Reference);
    }

    // ── Tactical, flag, branch edges ─────────────────────────────────────────

    [Fact]
    public void TacticalEdge_PlotFileParam_TargetsTacticalNode()
    {
        var graph = Build(Thread(UriA,
            "<Event Name=\"E\"><Event_Type>STORY_LAND_TACTICAL</Event_Type>" +
            "<Event_Param1>Story_Plots_M2_Land.xml</Event_Param1></Event>"));

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(StoryEdgeKind.Tactical, edge.Kind);
        Assert.Equal(EventId(UriA, "E"), edge.FromId);
        var target = Assert.Single(graph.Nodes, n => n.Id == edge.ToId);
        Assert.Equal(StoryNodeKind.TacticalPlot, target.Kind);
        Assert.Equal("Story_Plots_M2_Land.xml", target.Label);
    }

    [Fact]
    public void FlagEdges_ConnectWritersToReaders()
    {
        var graph = Build(Thread(UriA,
            "<Event Name=\"Writer\"><Reward_Type>SET_FLAG</Reward_Type>" +
            "<Reward_Param1>MyFlag</Reward_Param1></Event>" +
            "<Event Name=\"Reader\"><Event_Type>STORY_FLAG</Event_Type>" +
            "<Event_Param1>MyFlag OtherFlag</Event_Param1></Event>"));

        var edge = Assert.Single(graph.Edges, e => e.Kind == StoryEdgeKind.Flag);
        Assert.Equal(EventId(UriA, "Writer"), edge.FromId);
        Assert.Equal(EventId(UriA, "Reader"), edge.ToId);
        Assert.Equal("MyFlag", edge.Label);
    }

    [Fact]
    public void BranchParam_TargetsEveryBranchMember()
    {
        var graph = Build(Thread(UriA,
            "<Event Name=\"M1\"><Branch>Act1</Branch></Event>" +
            "<Event Name=\"M2\"><Branch>Act1</Branch></Event>" +
            "<Event Name=\"Off\"><Reward_Type>DISABLE_BRANCH</Reward_Type>" +
            "<Reward_Param1>Act1</Reward_Param1></Event>"));

        var branchEdges = graph.Edges.Where(e => e.Kind == StoryEdgeKind.Control).ToList();
        Assert.Equal(2, branchEdges.Count);
        Assert.All(branchEdges, e => Assert.Equal(EventId(UriA, "Off"), e.FromId));
        Assert.Contains(branchEdges, e => e.ToId == EventId(UriA, "M1"));
        Assert.Contains(branchEdges, e => e.ToId == EventId(UriA, "M2"));
    }

    [Fact]
    public void EventNodes_CarryModelAndThread()
    {
        var graph = Build(Thread(UriA, "<Event Name=\"A\"><Event_Type>STORY_TRIGGER</Event_Type></Event>"));

        var node = Assert.Single(graph.Nodes);
        Assert.Equal(StoryNodeKind.Event, node.Kind);
        Assert.Equal(UriA, node.ThreadUri);
        Assert.Equal("A", node.Event!.Name);
        Assert.Equal("A", node.Label);
    }
}

file sealed class StubSchemaProvider(params EnumDefinition[] enums) : ISchemaProvider
{
    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => enums;
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
        return enums.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public GameObjectTypeDefinition? GetObjectType(string t)
    {
        return null;
    }
}
