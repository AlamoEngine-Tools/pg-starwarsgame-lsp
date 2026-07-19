// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Discovery;
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

    private static StoryCampaignModel? Assemble(StoryChainScanResult chain, string name = "GC")
    {
        return new StoryCampaignAssembler(new NullSchema()).Assemble(name, chain, Reader(
            chain.Manifests.SelectMany(m => m.ActiveThreads.Concat(m.SuspendedThreads)).ToArray()));
    }

    [Fact]
    public void Assemble_UnknownCampaign_ReturnsNull()
    {
        Assert.Null(Assemble(Chain([], [])));
    }

    [Fact]
    public void Assemble_CollectsThreadsAcrossFactionManifests()
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

        var model = Assemble(chain)!;

        Assert.Equal(2, model.Threads.Count);
        Assert.Empty(model.SuspendedThreadUris);
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
    public void Assemble_ThreadActiveInOneManifest_IsNotSuspended()
    {
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

        var model = Assemble(chain)!;

        Assert.Empty(model.SuspendedThreadUris);
        Assert.Single(model.Threads);
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