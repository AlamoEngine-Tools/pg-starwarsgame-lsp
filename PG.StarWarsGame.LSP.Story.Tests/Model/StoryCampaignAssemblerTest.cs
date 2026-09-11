// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Tests.Model;

public sealed class StoryCampaignAssemblerTest
{
    private static StoryChainScanResult Chain(
        IReadOnlyList<StoryCampaignChain> campaigns,
        IReadOnlyList<StoryManifestContents> manifests,
        IReadOnlyList<StoryTacticalReference>? tactical = null)
    {
        return StoryChainScanResult.Empty with
        {
            Campaigns = campaigns,
            Manifests = manifests,
            TacticalReferences = tactical ?? []
        };
    }

    private static Func<string, (string, string)?> Reader(params string[] threadFiles)
    {
        var known = new HashSet<string>(threadFiles, StringComparer.OrdinalIgnoreCase);
        return rel => known.Contains(rel)
            ? ($"file:///xml/{rel.ToLowerInvariant()}", "<Story><Event Name=\"E_" + rel.Split('.')[0] + "\"/></Story>")
            : null;
    }

    /// <summary>A reader over thread bodies written out in full, for the collision fixtures.</summary>
    private static Func<string, (string, string)?> Reader(IReadOnlyDictionary<string, string> bodies)
    {
        return rel => bodies.TryGetValue(rel, out var body)
            ? ($"file:///xml/{rel.ToLowerInvariant()}", body)
            : null;
    }

    private static StoryCampaignModel? Assemble(
        StoryChainScanResult chain, string name = "GC", string faction = "Rebel")
    {
        return new StoryCampaignAssembler(new NullSchema()).Assemble(name, faction, chain, Reader(
            chain.Manifests.SelectMany(m => m.ActiveThreads.Concat(m.SuspendedThreads)).ToArray()));
    }

    [Fact]
    public void Assemble_UnknownCampaign_ReturnsNull()
    {
        Assert.Null(Assemble(Chain([], [])));
    }

    /// <summary>
    ///     A campaign declares a manifest per faction, and those are separate chains: the playable
    ///     faction's plots are what the player runs, and an unplayable faction's plots are triggered
    ///     by the AI. The engine never runs two of them as one story, so the model must not either.
    /// </summary>
    [Fact]
    public void Assemble_TakesOnlyTheNamedFactionsThreads()
    {
        var chain = Chain(
            [
                new StoryCampaignChain("GC", [
                    new StoryFactionManifest("Rebel", "M_R.xml"),
                    new StoryFactionManifest("Empire", "M_E.xml")
                ])
            ],
            [
                new StoryManifestContents("M_R.xml", ["T_R.xml"], [], []),
                new StoryManifestContents("M_E.xml", ["T_E.xml"], [], [])
            ]);

        var model = Assemble(chain, faction: "Rebel")!;

        var thread = Assert.Single(model.Threads);
        Assert.Contains("t_r.xml", thread.DocumentUri);
        Assert.Empty(model.SuspendedThreadUris);
    }

    [Fact]
    public void Assemble_CarriesTheFactionItWasAssembledFor()
    {
        var chain = Chain(
            [new StoryCampaignChain("GC", [new StoryFactionManifest("Empire", "M.xml")])],
            [new StoryManifestContents("M.xml", ["T.xml"], [], [])]);

        Assert.Equal("Empire", Assemble(chain, faction: "Empire")!.Faction);
    }

    [Fact]
    public void Assemble_FactionTheCampaignDoesNotDeclare_ReturnsNull()
    {
        var chain = Chain(
            [new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "M.xml")])],
            [new StoryManifestContents("M.xml", ["T.xml"], [], [])]);

        Assert.Null(Assemble(chain, faction: "Underworld"));
    }

    [Fact]
    public void Assemble_MatchesTheFactionHoweverTheXmlCasedIt()
    {
        var chain = Chain(
            [new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "M.xml")])],
            [new StoryManifestContents("M.xml", ["T.xml"], [], [])]);

        Assert.NotNull(Assemble(chain, faction: "rebel"));
    }

    /// <summary>
    ///     The defect this scoping exists to fix, in the shape the shipped data has it.
    ///     <para>
    ///         Measured over the FoC corpus: 48 campaigns declare more than one faction manifest,
    ///         and 36 cross-faction event-name collisions come from two DIFFERENT thread files -
    ///         every one of them <c>Universal_Story_Start</c>, which is each chain's ROOT. Merged,
    ///         <c>ResolveEventName</c> found two matches, raised "the engine's pick is undefined"
    ///         and drew a prereq edge into BOTH - so one faction's chain wired itself into the
    ///         other faction's root.
    ///     </para>
    /// </summary>
    [Fact]
    public void Assemble_EachFactionDefiningTheSameEventName_ResolvesWithinItsOwnFaction()
    {
        const string startAndFollower =
            "<Story><Event Name=\"Universal_Story_Start\"/>"
            + "<Event Name=\"Follower\"><Prereq>Universal_Story_Start</Prereq></Event></Story>";

        var chain = Chain(
            [
                new StoryCampaignChain("GC", [
                    new StoryFactionManifest("Rebel", "M_R.xml"),
                    new StoryFactionManifest("Empire", "M_E.xml")
                ])
            ],
            [
                new StoryManifestContents("M_R.xml", ["T_R.xml"], [], []),
                new StoryManifestContents("M_E.xml", ["T_E.xml"], [], [])
            ]);

        var read = Reader(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["T_R.xml"] = startAndFollower,
            ["T_E.xml"] = "<Story><Event Name=\"Universal_Story_Start\"/></Story>"
        });

        var rebel = new StoryCampaignAssembler(new NullSchema())
            .Assemble("GC", "Rebel", chain, read)!;

        Assert.DoesNotContain(rebel.Graph.Problems,
            p => p.Kind == StoryGraphProblemKind.AmbiguousTarget);

        // Every edge stays inside the Rebel thread; nothing reaches the Empire chain's root.
        Assert.All(rebel.Graph.Edges, e =>
        {
            Assert.DoesNotContain("t_e.xml", e.FromId);
            Assert.DoesNotContain("t_e.xml", e.ToId);
        });
    }

    [Fact]
    public void Assemble_ThreadOnlyEverSuspended_IsSuspended()
    {
        var chain = Chain(
            [new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "M.xml")])],
            [new StoryManifestContents("M.xml", ["T_Active.xml"], ["T_Susp.xml"], [])]);

        var model = Assemble(chain)!;

        var suspended = Assert.Single(model.SuspendedThreadUris);
        Assert.Contains("t_susp.xml", suspended);
    }

    [Fact]
    public void Assemble_ThreadListedBothWaysInOneManifest_IsNotSuspended()
    {
        // Active wins within a faction's own closure: the thread runs, and the suspended entry is
        // the manifest saying it may also be resumed.
        var chain = Chain(
            [new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "M.xml")])],
            [new StoryManifestContents("M.xml", ["T_Shared.xml"], ["T_Shared.xml"], [])]);

        var model = Assemble(chain)!;

        Assert.Empty(model.SuspendedThreadUris);
        Assert.Single(model.Threads);
    }

    [Fact]
    public void Assemble_ThreadSuspendedHereButActiveForAnotherFaction_IsSuspended()
    {
        // The merged model called this thread active because the OTHER faction's manifest said so.
        // A Rebel player never runs the Empire's manifest, so for the Rebel chain it is suspended.
        var chain = Chain(
            [
                new StoryCampaignChain("GC", [
                    new StoryFactionManifest("Rebel", "M1.xml"),
                    new StoryFactionManifest("Empire", "M2.xml")
                ])
            ],
            [
                new StoryManifestContents("M1.xml", [], ["T_Shared.xml"], []),
                new StoryManifestContents("M2.xml", ["T_Shared.xml"], [], [])
            ]);

        var model = Assemble(chain, faction: "Rebel")!;

        Assert.Single(model.SuspendedThreadUris);
    }

    [Fact]
    public void Assemble_FollowsTacticalReferencesIntoTheirManifests()
    {
        var chain = Chain(
            [new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "M.xml")])],
            [
                new StoryManifestContents("M.xml", ["T_Galactic.xml"], [], []),
                new StoryManifestContents("M_Tac.xml", ["T_Tactical.xml"], [], [])
            ],
            [new StoryTacticalReference("T_Galactic.xml", "M_Tac.xml")]);

        var model = Assemble(chain)!;

        Assert.Equal(2, model.Threads.Count);
    }

    [Fact]
    public void Assemble_RecordsTacticalManifestThreadUris()
    {
        var chain = Chain(
            [new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "M.xml")])],
            [
                new StoryManifestContents("M.xml", ["T_Galactic.xml"], [], []),
                new StoryManifestContents("M_Tac.xml", ["T_Tactical.xml"], [], [])
            ],
            [new StoryTacticalReference("T_Galactic.xml", "M_Tac.xml")]);

        var model = Assemble(chain)!;

        var entry = Assert.Single(model.TacticalManifestThreads);
        Assert.Equal("M_Tac.xml", entry.Key);
        var uri = Assert.Single(entry.Value);
        Assert.Equal("file:///xml/t_tactical.xml", uri);
    }

    [Fact]
    public void Assemble_MainCampaignManifests_AreNotRecordedAsTactical()
    {
        var chain = Chain(
            [new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "M.xml")])],
            [new StoryManifestContents("M.xml", ["T.xml"], [], [])]);

        var model = Assemble(chain)!;

        Assert.Empty(model.TacticalManifestThreads);
    }

    [Fact]
    public void Assemble_ManifestOfAnotherCampaign_IsExcluded()
    {
        var chain = Chain(
            [
                new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "M.xml")]),
                new StoryCampaignChain("Other", [new StoryFactionManifest("Rebel", "M_Other.xml")])
            ],
            [
                new StoryManifestContents("M.xml", ["T.xml"], [], []),
                new StoryManifestContents("M_Other.xml", ["T_Other.xml"], [], [])
            ]);

        var model = Assemble(chain)!;

        Assert.Single(model.Threads);
    }

    private sealed class NullSchema : ISchemaProvider
    {
        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
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

        public EnumDefinition? GetEnum(string e)
        {
            return null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }
    }
}