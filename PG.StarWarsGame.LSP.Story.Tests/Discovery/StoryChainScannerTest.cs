// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Story.Discovery;

namespace PG.StarWarsGame.LSP.Story.Tests.Discovery;

public sealed class StoryChainScannerTest
{
    private const string Registry = "campaignfiles.xml";

    // ── Fixture pieces ───────────────────────────────────────────────────────

    private static string CampaignRegistry(params string[] files)
    {
        return "<Campaign_Files>" +
               string.Concat(files.Select(f => $"<File>{f}</File>")) +
               "</Campaign_Files>";
    }

    private sealed class FakeResolver : IStoryChainFileResolver
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _baselineKnown = new(StringComparer.OrdinalIgnoreCase);

        public FakeResolver Add(string relPath, string content)
        {
            _files[relPath] = content;
            return this;
        }

        public FakeResolver BaselineKnown(string relPath)
        {
            _baselineKnown.Add(relPath);
            return this;
        }

        public StoryChainFile? ReadFile(string xmlRelativePath)
        {
            return _files.TryGetValue(xmlRelativePath, out var content)
                ? new StoryChainFile(content, "file:///xml/" + xmlRelativePath.ToLowerInvariant())
                : null;
        }

        public bool IsKnownToBaseline(string xmlRelativePath)
        {
            return _baselineKnown.Contains(xmlRelativePath);
        }
    }

    private static StoryChainScanResult Scan(FakeResolver resolver)
    {
        return new StoryChainScanner(resolver).Scan(Registry);
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Scan_FullChain_DiscoversManifestsThreadsAndLuaScripts()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                """
                <Campaigns>
                  <Campaign Name="Test">
                    <Rebel_Story_Name>Story_Plots_Rebel.xml</Rebel_Story_Name>
                    <Empire_Story_Name> Story_Plots_Empire.xml </Empire_Story_Name>
                  </Campaign>
                </Campaigns>
                """)
            .Add("Story_Plots_Rebel.xml",
                """
                <Story_Mode_Plots>
                  <Active_Plot>Story_Rebel_Act_I.xml</Active_Plot>
                  <Suspended_Plot>Story_Rebel_Act_II.xml</Suspended_Plot>
                  <Lua_Script>Story_Rebel_Act_III</Lua_Script>
                </Story_Mode_Plots>
                """)
            .Add("Story_Plots_Empire.xml",
                "<Story_Mode_Plots><Active_Plot>Story_Empire_Act_I.xml</Active_Plot></Story_Mode_Plots>")
            .Add("Story_Rebel_Act_I.xml", "<Story_Threads><Event Name=\"E1\"/></Story_Threads>")
            .Add("Story_Rebel_Act_II.xml", "<Story_Threads/>")
            .Add("Story_Empire_Act_I.xml", "<Story_Threads/>");

        var result = Scan(resolver);

        Assert.Equal(["Story_Plots_Rebel.xml", "Story_Plots_Empire.xml"], result.ManifestFiles);
        Assert.Equal(["Story_Rebel_Act_I.xml", "Story_Rebel_Act_II.xml", "Story_Empire_Act_I.xml"],
            result.ThreadFiles);
        Assert.Equal(["Story_Rebel_Act_III"], result.LuaScripts);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Scan_TacticalReferences_RecurseIntoTacticalManifests()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Rebel.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_Rebel.xml",
                "<Story_Mode_Plots><Active_Plot>Story_Galactic.xml</Active_Plot></Story_Mode_Plots>")
            .Add("Story_Galactic.xml",
                """
                <Story_Threads>
                  <Event Name="Trigger_M1_Space">
                    <Event_Type>STORY_SPACE_TACTICAL</Event_Type>
                    <Event_Param1>Story_Plots_M1_Space.xml</Event_Param1>
                  </Event>
                  <Event Name="Link_M2">
                    <Event_Type>STORY_TRIGGER</Event_Type>
                    <Reward_Type>LINK_TACTICAL</Reward_Type>
                    <Reward_Param1>Kuat</Reward_Param1>
                    <Reward_Param7>Story_Plots_M2_Land.xml</Reward_Param7>
                  </Event>
                </Story_Threads>
                """)
            .Add("Story_Plots_M1_Space.xml",
                "<Story_Mode_Plots><Active_Plot>Story_M1_Space.xml</Active_Plot></Story_Mode_Plots>")
            .Add("Story_Plots_M2_Land.xml",
                "<Story_Mode_Plots><Active_Plot>Story_M2_Land.xml</Active_Plot></Story_Mode_Plots>")
            .Add("Story_M1_Space.xml", "<Story_Threads/>")
            .Add("Story_M2_Land.xml", "<Story_Threads/>");

        var result = Scan(resolver);

        Assert.Equal(
            ["Story_Plots_Rebel.xml", "Story_Plots_M1_Space.xml", "Story_Plots_M2_Land.xml"],
            result.ManifestFiles);
        Assert.Equal(["Story_Galactic.xml", "Story_M1_Space.xml", "Story_M2_Land.xml"],
            result.ThreadFiles);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Scan_CyclicTacticalReferences_TerminateWithoutDuplicates()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_A.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_A.xml",
                "<Story_Mode_Plots><Active_Plot>Story_A.xml</Active_Plot></Story_Mode_Plots>")
            .Add("Story_A.xml",
                "<Story_Threads><Event Name=\"E\">" +
                "<Event_Type>STORY_LAND_TACTICAL</Event_Type>" +
                "<Event_Param1>Story_Plots_A.xml</Event_Param1>" +
                "</Event></Story_Threads>");

        var result = Scan(resolver);

        Assert.Equal(["Story_Plots_A.xml"], result.ManifestFiles);
        Assert.Equal(["Story_A.xml"], result.ThreadFiles);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Scan_SameManifestReferencedWithDifferentCasing_IsDeduplicated()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"A\">" +
                "<Rebel_Story_Name>Story_Plots_Shared.xml</Rebel_Story_Name>" +
                "</Campaign><Campaign Name=\"B\">" +
                "<Rebel_Story_Name>STORY_PLOTS_SHARED.XML</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_Shared.xml", "<Story_Mode_Plots/>");

        var result = Scan(resolver);

        Assert.Equal(["Story_Plots_Shared.xml"], result.ManifestFiles);
        Assert.Empty(result.Problems);
    }

    // ── Fallbacks and problems ───────────────────────────────────────────────

    [Fact]
    public void Scan_MissingCampaignRegistry_ReturnsEmpty()
    {
        var result = Scan(new FakeResolver());

        Assert.Equal(StoryChainScanResult.Empty, result);
    }

    [Fact]
    public void Scan_ManifestMissingButKnownToBaseline_IsRegisteredWithoutProblem()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Vanilla.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .BaselineKnown("Story_Plots_Vanilla.xml");

        var result = Scan(resolver);

        Assert.Equal(["Story_Plots_Vanilla.xml"], result.ManifestFiles);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Scan_UnresolvableStoryName_ProducesErrorAtValuePosition()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns>\n" +
                "  <Campaign Name=\"T\">\n" +
                "    <Rebel_Story_Name>Story_Plots_Gone.xml</Rebel_Story_Name>\n" +
                "  </Campaign>\n" +
                "</Campaigns>");

        var result = Scan(resolver);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(StoryChainProblemKind.UnresolvedStoryName, problem.Kind);
        Assert.Equal("Campaigns_Test.xml", problem.SourceFile);
        Assert.Equal("file:///xml/campaigns_test.xml", problem.DocumentUri);
        Assert.Equal("Story_Plots_Gone.xml", problem.Reference);
        Assert.Equal(XmlDiagnosticSeverity.Error, problem.Severity);
        Assert.Equal(2, problem.Line);
        Assert.Equal("    <Rebel_Story_Name>".Length, problem.Column);
        Assert.Equal(2, problem.EndLine);
        Assert.Equal(problem.Column + "Story_Plots_Gone.xml".Length, problem.EndColumn);
        Assert.Empty(result.ManifestFiles);
    }

    [Fact]
    public void Scan_UnresolvableReferenceInBaselineKnownSource_IsWarning()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Vanilla.xml"))
            .Add("Campaigns_Vanilla.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Legacy.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .BaselineKnown("Campaigns_Vanilla.xml");

        var result = Scan(resolver);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(XmlDiagnosticSeverity.Warning, problem.Severity);
    }

    [Fact]
    public void Scan_UnresolvablePlotEntry_ProducesProblemInManifest()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Rebel.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_Rebel.xml",
                "<Story_Mode_Plots>\n" +
                "  <Active_Plot>Story_Missing.xml</Active_Plot>\n" +
                "</Story_Mode_Plots>");

        var result = Scan(resolver);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(StoryChainProblemKind.UnresolvedPlotEntry, problem.Kind);
        Assert.Equal("Story_Plots_Rebel.xml", problem.SourceFile);
        Assert.Equal("Story_Missing.xml", problem.Reference);
        Assert.Equal(1, problem.Line);
        Assert.Equal("  <Active_Plot>".Length, problem.Column);
        Assert.Empty(result.ThreadFiles);
    }

    [Fact]
    public void Scan_UnresolvableTacticalReference_ProducesProblemInThread()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Rebel.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_Rebel.xml",
                "<Story_Mode_Plots><Active_Plot>Story_Galactic.xml</Active_Plot></Story_Mode_Plots>")
            .Add("Story_Galactic.xml",
                "<Story_Threads><Event Name=\"E\">" +
                "<Event_Type>STORY_SPACE_TACTICAL</Event_Type>" +
                "<Event_Param1>Story_Plots_Gone.xml</Event_Param1>" +
                "</Event></Story_Threads>");

        var result = Scan(resolver);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(StoryChainProblemKind.UnresolvedTacticalReference, problem.Kind);
        Assert.Equal("Story_Galactic.xml", problem.SourceFile);
        Assert.Equal("Story_Plots_Gone.xml", problem.Reference);
    }

    [Fact]
    public void Scan_ManifestWithoutStoryModePlotsRoot_ReportsMalformedAtReferencingEntry()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Broken.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_Broken.xml", "<Wrong_Root/>");

        var result = Scan(resolver);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(StoryChainProblemKind.MalformedManifest, problem.Kind);
        Assert.Equal("Campaigns_Test.xml", problem.SourceFile);
        Assert.Equal("Story_Plots_Broken.xml", problem.Reference);
        // The file is still typed as a manifest so the user gets tag-level validation inside it.
        Assert.Equal(["Story_Plots_Broken.xml"], result.ManifestFiles);
    }

    [Fact]
    public void Scan_TacticalEventWithoutParam_IsIgnored()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Rebel.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_Rebel.xml",
                "<Story_Mode_Plots><Active_Plot>Story_Galactic.xml</Active_Plot></Story_Mode_Plots>")
            .Add("Story_Galactic.xml",
                "<Story_Threads><Event Name=\"E\">" +
                "<Event_Type>STORY_SPACE_TACTICAL</Event_Type>" +
                "<Event_Param2>MonCalimari</Event_Param2>" +
                "</Event><Event Name=\"F\">" +
                "<Reward_Type>LINK_TACTICAL</Reward_Type>" +
                "<Reward_Param7></Reward_Param7>" +
                "</Event></Story_Threads>");

        var result = Scan(resolver);

        Assert.Equal(["Story_Plots_Rebel.xml"], result.ManifestFiles);
        Assert.Equal(["Story_Galactic.xml"], result.ThreadFiles);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Scan_MissingCampaignFile_IsSkippedSilently()
    {
        // Campaign files themselves are the fileRegistry definition's concern; the chain scan
        // only reports problems for the story-specific links.
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Gone.xml", "Campaigns_Here.xml"))
            .Add("Campaigns_Here.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Rebel.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_Rebel.xml", "<Story_Mode_Plots/>");

        var result = Scan(resolver);

        Assert.Equal(["Story_Plots_Rebel.xml"], result.ManifestFiles);
        Assert.Empty(result.Problems);
    }

    // ── Structured campaign associations (feed the per-campaign story models) ─

    [Fact]
    public void Scan_RecordsCampaignFactionManifestAssociations()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"GC_One\">" +
                "<Rebel_Story_Name>Story_Plots_R.xml</Rebel_Story_Name>" +
                "<Empire_Story_Name>Story_Plots_E.xml</Empire_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_R.xml", "<Story_Mode_Plots/>")
            .Add("Story_Plots_E.xml", "<Story_Mode_Plots/>");

        var result = Scan(resolver);

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal("GC_One", campaign.Name);
        Assert.Equal([("Rebel", "Story_Plots_R.xml"), ("Empire", "Story_Plots_E.xml")],
            campaign.FactionManifests.Select(f => (f.Faction, f.ManifestFile)));
    }

    [Fact]
    public void Scan_RecordsManifestContents_ThreadsAndLuaScripts()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_R.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_R.xml",
                "<Story_Mode_Plots><Active_Plot>Story_Act_I.xml</Active_Plot>" +
                "<Suspended_Plot>Story_Act_II.xml</Suspended_Plot>" +
                "<Lua_Script>Story_Act_III</Lua_Script></Story_Mode_Plots>")
            .Add("Story_Act_I.xml", "<Story_Threads/>")
            .Add("Story_Act_II.xml", "<Story_Threads/>");

        var result = Scan(resolver);

        var manifest = Assert.Single(result.Manifests);
        Assert.Equal("Story_Plots_R.xml", manifest.ManifestFile);
        Assert.Equal(["Story_Act_I.xml"], manifest.ActiveThreads);
        Assert.Equal(["Story_Act_II.xml"], manifest.SuspendedThreads);
        Assert.Equal(["Story_Act_III"], manifest.LuaScripts);
    }

    [Fact]
    public void Scan_RecordsTacticalManifestReferencesPerThread()
    {
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("Campaigns_Test.xml"))
            .Add("Campaigns_Test.xml",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_R.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_R.xml",
                "<Story_Mode_Plots><Active_Plot>Story_Galactic.xml</Active_Plot></Story_Mode_Plots>")
            .Add("Story_Galactic.xml",
                "<Story_Threads><Event Name=\"E\">" +
                "<Event_Type>STORY_SPACE_TACTICAL</Event_Type>" +
                "<Event_Param1>Story_Plots_M1.xml</Event_Param1>" +
                "</Event></Story_Threads>")
            .Add("Story_Plots_M1.xml", "<Story_Mode_Plots/>");

        var result = Scan(resolver);

        Assert.Equal([("Story_Galactic.xml", "Story_Plots_M1.xml")],
            result.TacticalReferences.Select(t => (t.ThreadFile, t.ManifestFile)));
    }

    [Fact]
    public void Scan_RegistryEntriesWithGamePathPrefix_AreNormalizedToXmlRelative()
    {
        // Vanilla campaignfiles.xml lists entries as "DATA\XML\CAMPAIGNS_UNDERWORLD_GC.XML".
        var resolver = new FakeResolver()
            .Add(Registry, CampaignRegistry("DATA\\XML\\CAMPAIGNS_TEST.XML"))
            .Add("CAMPAIGNS_TEST.XML",
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Rebel.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>")
            .Add("Story_Plots_Rebel.xml", "<Story_Mode_Plots/>");

        var result = Scan(resolver);

        Assert.Equal(["Story_Plots_Rebel.xml"], result.ManifestFiles);
    }
}
