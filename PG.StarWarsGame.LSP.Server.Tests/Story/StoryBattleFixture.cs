// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

/// <summary>
///     A campaign with two battles, for every server-side test of scopes: the galaxy links M2 in
///     from E and M5 from E2, listens for M2's victory in Win, and reads a flag M2 writes; M2 is an
///     ambush that calls reinforcements which set the flag; M5 is one event.
/// </summary>
internal static class StoryBattleFixture
{
    // Document URIs keep the casing the files have on disk; the manifests write the same names
    // in the engine's casing, which is never the same. The model resolves one to the other once.
    public const string Galaxy = "file:///ws/data/xml/Story_Campaign.xml";
    public const string Battle = "file:///ws/data/xml/story_m2_land.xml";
    public const string Battle2 = "file:///ws/data/xml/Story_M5_Space.xml";
    public const string GalaxyManifestEntry = "STORY_CAMPAIGN.XML";
    public const string M2ManifestEntry = "Story_M2_LAND.xml";
    public const string M5ManifestEntry = "story_m5_space.xml";
    public const string FactionManifest = "Story_Plots_Empire.xml";
    public const string M2 = "Story_Plots_M2_Land.xml";
    public const string M5 = "Story_Plots_M5_Space.xml";
    public const string M2Key = "story_plots_m2_land.xml";
    public const string M5Key = "story_plots_m5_space.xml";

    public const string GalaxyText =
        "<Story>" +
        "<Event Name=\"Begin\"><Event_Type>STORY_ELAPSED</Event_Type><Event_Param1>0</Event_Param1></Event>" +
        "<Event Name=\"E\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>LINK_TACTICAL</Reward_Type><Reward_Param1>" + M2 + "</Reward_Param1></Event>" +
        "<Event Name=\"E2\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Begin</Prereq>" +
        "<Reward_Type>LINK_TACTICAL</Reward_Type><Reward_Param1>" + M5 + "</Reward_Param1></Event>" +
        "<Event Name=\"Win\"><Event_Type>STORY_VICTORY</Event_Type><Prereq>E</Prereq></Event>" +
        "<Event Name=\"Reader\"><Event_Type>STORY_FLAG</Event_Type><Event_Param1>F</Event_Param1>" +
        "<Event_Param2>1</Event_Param2></Event>" +
        "</Story>";

    // Outcome is the battle's own victory listener, armed from load as the tutorial's is: it writes
    // W whether the battle was played or decided on the portal.
    public const string BattleText =
        "<Story>" +
        "<Event Name=\"Ambush\"><Event_Type>STORY_GENERIC</Event_Type></Event>" +
        "<Event Name=\"Reinforcements\"><Event_Type>STORY_TRIGGER</Event_Type><Prereq>Ambush</Prereq>" +
        "<Reward_Type>SET_FLAG</Reward_Type><Reward_Param1>F</Reward_Param1></Event>" +
        "<Event Name=\"Outcome\"><Event_Type>STORY_VICTORY</Event_Type>" +
        "<Reward_Type>SET_FLAG</Reward_Type><Reward_Param1>W</Reward_Param1></Event>" +
        "</Story>";

    public const string Battle2Text =
        "<Story><Event Name=\"Space\"><Event_Type>STORY_GENERIC</Event_Type></Event></Story>";

    public static string Id(string uri, string name)
    {
        return StoryGraphBuilder.EventNodeId(uri, name);
    }

    public static StoryCampaignModel BuildModel()
    {
        var threads = new List<StoryThread>
        {
            StoryThreadParser.Parse(GalaxyText, Galaxy),
            StoryThreadParser.Parse(BattleText, Battle),
            StoryThreadParser.Parse(Battle2Text, Battle2)
        };
        var manifests = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [M2] = new HashSet<string> { Battle },
            [M5] = new HashSet<string> { Battle2 }
        };
        var manifestFiles = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [M2] = [M2ManifestEntry],
            [M5] = [M5ManifestEntry]
        };
        var graph = new StoryGraphBuilder(new Schema()).Build(threads, manifests);
        return new StoryCampaignModel("GC", "Empire", threads, new HashSet<string>(StringComparer.Ordinal), graph)
        {
            TacticalManifestThreads = manifests,
            ThreadUriByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [GalaxyManifestEntry] = Galaxy,
                [M2ManifestEntry] = Battle,
                [M5ManifestEntry] = Battle2
            },
            Battles = StoryGraphScoper.Battles(graph, manifests, manifestFiles)
        };
    }

    /// <summary>The chain as the scanner would report it: one campaign, one faction, three manifests.</summary>
    public static StoryChainScanResult Chain()
    {
        return StoryChainScanResult.Empty with
        {
            Campaigns = [new StoryCampaignChain("GC", [new StoryFactionManifest("Empire", FactionManifest)])],
            Manifests =
            [
                new StoryManifestContents(FactionManifest, [GalaxyManifestEntry], [], []),
                new StoryManifestContents(M2, [M2ManifestEntry], [], []),
                new StoryManifestContents(M5, [M5ManifestEntry], [], [])
            ]
        };
    }

    public sealed class ModelService : IStoryModelService
    {
        private static readonly StoryCampaignModel Model = BuildModel();

        public IReadOnlyList<string> GetCampaignNames()
        {
            return ["GC"];
        }

        public IReadOnlyList<StoryModelKey> GetModelKeys()
        {
            return [new StoryModelKey("GC", "Empire")];
        }

        public StoryCampaignModel? GetCampaignModel(string campaignName, string faction)
        {
            return campaignName == "GC" ? Model : null;
        }

        public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
        {
            return [];
        }

        public StoryChainScanResult GetChainResult()
        {
            return Chain();
        }

        public IReadOnlyList<string> GetInvalidatedCampaigns()
        {
            return [];
        }
    }

    public sealed class IndexService : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task OpenDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return new NoopDisposable();
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    public sealed class Schema : ISchemaProvider
    {
        private static readonly EnumDefinition Events = new()
        {
            Name = "StoryEventType",
            Values =
            [
                new EnumValueDefinition { Name = "STORY_TRIGGER" },
                new EnumValueDefinition { Name = "STORY_ELAPSED" },
                new EnumValueDefinition { Name = "STORY_GENERIC" },
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
                        {
                            Position = 0, ValueType = XmlValueType.NameReference, ReferenceTypeName = "StoryPlotFile"
                        }
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
}