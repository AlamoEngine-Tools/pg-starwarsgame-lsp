// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Tests.Graph;

public sealed class StoryGraphScoperTest
{
    private const string Galaxy = "file:///ws/data/xml/story_campaign.xml";
    private const string Battle = "file:///ws/data/xml/story_m2_land.xml";
    private const string Battle2 = "file:///ws/data/xml/story_m5_space.xml";
    private const string M2 = "Story_Plots_M2_Land.xml";
    private const string M5 = "Story_Plots_M5_Space.xml";

    private static ISchemaProvider Schema { get; } = new ScoperSchemaProvider();

    // The galaxy links M2 in from E, listens for its victory in Win, and reads a flag M2 writes.
    private const string GalaxyText =
        "<Story>" +
        "<Event Name=\"Begin\"><Event_Type>STORY_ELAPSED</Event_Type></Event>" +
        "<Event Name=\"E\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>LINK_TACTICAL</Reward_Type><Reward_Param1>" + M2 + "</Reward_Param1></Event>" +
        "<Event Name=\"Win\"><Event_Type>STORY_VICTORY</Event_Type><Prereq>E</Prereq></Event>" +
        "<Event Name=\"Reader\"><Event_Type>STORY_FLAG</Event_Type><Event_Param1>F</Event_Param1></Event>" +
        "</Story>";

    private const string BattleText =
        "<Story>" +
        "<Event Name=\"Ambush\"><Event_Type>STORY_TRIGGER</Event_Type></Event>" +
        "<Event Name=\"Reinforcements\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Ambush</Prereq>" +
        "<Reward_Type>SET_FLAG</Reward_Type><Reward_Param1>F</Reward_Param1></Event>" +
        "</Story>";

    private static StoryCampaignModel Model(params (string Uri, string Text)[] extra)
    {
        var threads = new List<StoryThread>
        {
            StoryThreadParser.Parse(GalaxyText, Galaxy),
            StoryThreadParser.Parse(BattleText, Battle)
        };
        threads.AddRange(extra.Select(e => StoryThreadParser.Parse(e.Text, e.Uri)));
        var manifests = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [M2] = new HashSet<string> { Battle }
        };
        if (extra.Any(e => e.Uri == Battle2)) manifests[M5] = new HashSet<string> { Battle2 };
        var graph = new StoryGraphBuilder(Schema).Build(threads, manifests);
        return new StoryCampaignModel("GC", "Empire", threads, new HashSet<string>(StringComparer.Ordinal), graph)
        {
            TacticalManifestThreads = manifests
        };
    }

    private static string Id(string uri, string name)
    {
        return StoryGraphBuilder.EventNodeId(uri, name);
    }

    [Fact]
    public void GalacticScope_KeepsTheGalaxyAndTheBattleAsOnePortal()
    {
        var scoped = StoryGraphScoper.Scope(Model(), null);

        var events = scoped.Nodes.Where(n => n.Kind == StoryNodeKind.Event).Select(n => n.Label).Order().ToList();
        Assert.Equal(["Begin", "E", "Reader", "Win"], events);
        var stub = Assert.Single(scoped.Nodes, n => n.Kind == StoryNodeKind.TacticalPlot);
        Assert.Contains(scoped.Edges,
            e => e.Kind == StoryEdgeKind.Tactical && e.FromId == Id(Galaxy, "E") && e.ToId == stub.Id);
        Assert.DoesNotContain(scoped.Edges, e => e.Kind == StoryEdgeKind.TacticalEntry);
        // The battle's flag write reaches the galactic reader through the portal.
        Assert.Contains(scoped.Edges,
            e => e.Kind == StoryEdgeKind.Flag && e.FromId == stub.Id && e.ToId == Id(Galaxy, "Reader"));
        Assert.DoesNotContain(scoped.Nodes, n => n.ThreadUri == Battle);
    }

    [Fact]
    public void BattleScope_ShowsTheBattleWithEntryAndExitPortals()
    {
        var scoped = StoryGraphScoper.Scope(Model(), M2);

        var events = scoped.Nodes.Where(n => n.Kind == StoryNodeKind.Event).Select(n => n.Label).Order().ToList();
        Assert.Equal(["Ambush", "Reinforcements"], events);
        var portals = scoped.Nodes.Where(n => n.Kind == StoryNodeKind.GalacticPortal).ToList();
        var entry = Assert.Single(portals, p => p.PortalTarget == Id(Galaxy, "E"));
        var exit = Assert.Single(portals, p => p.PortalTarget == Id(Galaxy, "Win"));
        var reader = Assert.Single(portals, p => p.PortalTarget == Id(Galaxy, "Reader"));
        Assert.Contains(scoped.Edges,
            e => e.Kind == StoryEdgeKind.TacticalEntry && e.FromId == entry.Id && e.ToId == Id(Battle, "Ambush"));
        Assert.Contains(scoped.Edges,
            e => e.Kind == StoryEdgeKind.Tactical && e.FromId == entry.Id && e.ToId == exit.Id);
        Assert.Contains(scoped.Edges,
            e => e.Kind == StoryEdgeKind.Flag && e.FromId == Id(Battle, "Reinforcements") && e.ToId == reader.Id);
        Assert.DoesNotContain(scoped.Nodes, n => n.Kind == StoryNodeKind.TacticalPlot);
        Assert.DoesNotContain(scoped.Nodes, n => n.ThreadUri == Galaxy);
    }

    [Fact]
    public void Scope_KeyMatchesHoweverTheReferenceIsWritten_AndUnknownIsEmpty()
    {
        var model = Model();
        Assert.NotEmpty(StoryGraphScoper.Scope(model, "DATA\\XML\\story_plots_m2_land.xml").Nodes);
        Assert.Empty(StoryGraphScoper.Scope(model, "story_plots_nowhere.xml").Nodes);
    }

    [Fact]
    public void Battles_RankInTheOrderTheGalaxyReachesThem_NotFileOrder()
    {
        // M5 sorts after M2 by file but is linked from a deeper event; and a second galaxy thread
        // links M5 - "Early" links it from a root, so M5 comes FIRST although its file sorts later.
        const string second = "file:///ws/data/xml/story_early.xml";
        var model = Model(
            (Battle2, "<Story><Event Name=\"Space\"><Event_Type>STORY_TRIGGER</Event_Type></Event></Story>"),
            (second, "<Story><Event Name=\"Early\"><Event_Type>STORY_TRIGGER</Event_Type>" +
                     "<Reward_Type>LINK_TACTICAL</Reward_Type><Reward_Param1>" + M5 +
                     "</Reward_Param1></Event></Story>"));

        var battles = StoryGraphScoper.Battles(model.Graph, model.TacticalManifestThreads);

        Assert.Equal(2, battles.Count);
        // Roots are visited in thread order: the galaxy thread first, so Begin -> E (visit 1) beats
        // Early (visited after the whole first thread).
        // The label keeps the manifest's own casing; the KEY is the normalised one.
        Assert.Equal(["Story_Plots_M2_Land", "Story_Plots_M5_Space"],
            battles.OrderBy(b => b.Rank).Select(b => b.Label));
        var m2 = battles.Single(b => b.Key == "story_plots_m2_land.xml");
        Assert.Equal([Id(Galaxy, "E")], m2.EntryEventIds);
        Assert.Equal(new HashSet<string> { Battle }, m2.ThreadUris);
    }

    [Fact]
    public void Scope_WithoutBattles_IsTheWholeGraph()
    {
        var threads = new List<StoryThread> { StoryThreadParser.Parse(GalaxyText, Galaxy) };
        var graph = new StoryGraphBuilder(Schema).Build(threads);
        var model = new StoryCampaignModel("GC", "Empire", threads, new HashSet<string>(StringComparer.Ordinal), graph);

        Assert.Same(graph, StoryGraphScoper.Scope(model, null));
        Assert.Empty(StoryGraphScoper.Scope(model, M2).Nodes);
        Assert.Empty(StoryGraphScoper.Battles(graph, model.TacticalManifestThreads));
    }
}

file sealed class ScoperSchemaProvider : ISchemaProvider
{
    private static readonly EnumDefinition Events = new()
    {
        Name = "StoryEventType",
        Values =
        [
            new EnumValueDefinition { Name = "STORY_TRIGGER" },
            new EnumValueDefinition { Name = "STORY_ELAPSED" },
            new EnumValueDefinition { Name = "STORY_VICTORY" },
            new EnumValueDefinition
            {
                Name = "STORY_FLAG",
                Params =
                [
                    new ParamDefinition
                        { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "StoryFlag" }
                ]
            }
        ]
    };

    private static readonly EnumDefinition Rewards = new()
    {
        Name = "StoryRewardType",
        Values =
        [
            new EnumValueDefinition
            {
                Name = "LINK_TACTICAL",
                Params =
                [
                    new ParamDefinition
                        { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "StoryPlotFile" }
                ]
            },
            new EnumValueDefinition
            {
                Name = "SET_FLAG",
                Params =
                [
                    new ParamDefinition
                        { Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "StoryFlag" }
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
        return name switch
        {
            "StoryEventType" => Events,
            "StoryRewardType" => Rewards,
            _ => null
        };
    }

    public GameObjectTypeDefinition? GetObjectType(string t)
    {
        return null;
    }
}