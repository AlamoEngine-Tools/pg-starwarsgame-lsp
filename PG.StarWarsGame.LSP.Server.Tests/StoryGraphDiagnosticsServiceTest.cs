// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class StoryGraphDiagnosticsServiceTest
{
    private const string ThreadUri = "file:///ws/data/xml/story_a.xml";

    private static StoryGraphDiagnosticsService Build()
    {
        return new StoryGraphDiagnosticsService(new StubModelService(), new SchemaProviderBuilder().Build(),
            ResxMessageCatalog.Server("en"));
    }

    [Fact]
    public void GetForDocument_SurfacesModelDiagnostics()
    {
        var diagnostics = Build().GetForDocument(ThreadUri);

        // The stub campaign contains a dangling prereq in this document.
        Assert.Contains(diagnostics, d => d.Message.Contains("Ghost"));
    }

    [Fact]
    public void GetForDocument_SameFindingFromTwoCampaigns_IsDeduplicated()
    {
        var diagnostics = Build().GetForDocument(ThreadUri);

        Assert.Single(diagnostics, d => d.Message.Contains("Ghost"));
    }

    private sealed class StubModelService : IStoryModelService
    {
        // Two campaigns share the same thread - findings must not double up.
        public IReadOnlyList<string> GetCampaignNames()
        {
            return ["GC_One", "GC_Two"];
        }

        public IReadOnlyList<StoryModelKey> GetModelKeys()
        {
            return GetCampaignNames()
                .Select(c => new StoryModelKey(c, "Rebel")).ToList();
        }

        public StoryCampaignModel GetCampaignModel(string campaignName, string faction)
        {
            var thread = StoryThreadParser.Parse(
                "<Story><Event Name=\"B\"><Prereq>Ghost</Prereq></Event></Story>", ThreadUri);
            return new StoryCampaignModel(campaignName, faction, [thread],
                new HashSet<string>(StringComparer.Ordinal),
                new StoryGraphBuilder(new SchemaProviderBuilder().Build()).Build([thread]));
        }

        public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
        {
            return GetModelKeys()
                .Select(k => GetCampaignModel(k.Campaign, k.Faction))
                .ToList();
        }

        public StoryChainScanResult GetChainResult()
        {
            return StoryChainScanResult.Empty;
        }

        public IReadOnlyList<string> GetInvalidatedCampaigns()
        {
            return [];
        }
    }
}