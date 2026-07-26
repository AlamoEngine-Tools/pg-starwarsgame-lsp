// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.CrossTagRules;

/// <summary>
///     Shape half of the campaign story-attachment rule: a <c>{Faction}_Story_Name</c> tag takes a
///     single plot file, while the generic <c>Story_Name</c> takes a
///     <c>Faction, PlotFile</c> tuple list. Pasting the tuple form into the faction-specific tag is
///     silently accepted by the engine's file lookup and only ever surfaced (much later, and
///     misleadingly) as "file does not exist" by the startup story-chain scan.
/// </summary>
public sealed class CampaignStoryAttachmentRuleTest
{
    private const string Uri = "file:///Campaigns/Campaigns_Test.xml";

    private static XmlDocumentFactProducer BuildProducer()
    {
        return new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new NoFileTypeRegistry(),
            new XmlStructuralValidator(),
            [new CampaignStoryAttachmentRule()]);
    }

    private static IReadOnlyList<CampaignStoryAttachmentFact> Facts(string xml)
    {
        return BuildProducer().Produce(xml, Uri).OfType<CampaignStoryAttachmentFact>().ToList();
    }

    private static string Campaign(string body)
    {
        return "<Campaigns>\n  <Campaign Name=\"Test\">\n" + body + "  </Campaign>\n</Campaigns>\n";
    }

    [Fact]
    public void FactionSpecificTag_with_a_single_plot_file_emits_no_fact()
    {
        Assert.Empty(Facts(Campaign("    <Rebel_Story_Name>Conquests\\Story_Plots_R.xml</Rebel_Story_Name>\n")));
    }

    [Fact]
    public void GenericTag_with_a_faction_plotfile_tuple_emits_no_shape_fact()
    {
        var facts = Facts(Campaign("    <Story_Name>Rebel, Conquests\\Story_Plots_R.xml</Story_Name>\n"));
        Assert.DoesNotContain(facts, f => f.Problem == CampaignStoryAttachmentProblem.TupleInFactionSpecificTag);
    }

    [Theory]
    [InlineData("Rebel_Story_Name", "Rebel")]
    [InlineData("Empire_Story_Name", "Empire")]
    [InlineData("Underworld_Story_Name", "Underworld")]
    public void FactionSpecificTag_holding_a_tuple_emits_a_shape_fact(string tagName, string faction)
    {
        var facts = Facts(Campaign(
            $"    <{tagName}>test, Conquests\\Story_Plots_GCMenu.xml</{tagName}>\n"));

        var fact = Assert.Single(facts);
        Assert.Equal(CampaignStoryAttachmentProblem.TupleInFactionSpecificTag, fact.Problem);
        Assert.Equal(faction, fact.Faction);
        Assert.Equal(tagName, fact.TagName, ignoreCase: true);
        Assert.Equal("test, Conquests\\Story_Plots_GCMenu.xml", fact.Value);
        Assert.Equal(Uri, fact.DocumentUri);
    }

    [Fact]
    public void Shape_fact_range_covers_the_value_not_the_tag()
    {
        const string tag = "    <Rebel_Story_Name>";
        var fact = Assert.Single(Facts(Campaign(tag + "test, Story_Plots_R.xml</Rebel_Story_Name>\n")));

        Assert.Equal(2, fact.Line); // 0-based: <Campaigns>, <Campaign>, then the tag
        Assert.Equal(tag.Length, fact.Column);
        Assert.Equal("test, Story_Plots_R.xml".Length, fact.Length);
    }

    [Fact]
    public void Story_name_tags_outside_a_Campaign_element_are_not_evaluated()
    {
        // Malformed nesting is the structural validator's concern; correlating attachments only
        // means anything inside the <Campaign> that owns them.
        Assert.Empty(Facts("<Campaigns>\n  <Rebel_Story_Name>a, b.xml</Rebel_Story_Name>\n</Campaigns>\n"));
    }

    [Fact]
    public void Unrelated_campaign_tags_emit_no_fact()
    {
        Assert.Empty(Facts(Campaign("    <Text_ID>TEXT_CAMPAIGN_TEST</Text_ID>\n")));
    }

    // ── Faction correlation ──────────────────────────────────────────────────

    [Fact]
    public void One_attachment_per_faction_emits_no_fact()
    {
        Assert.Empty(Facts(Campaign(
            "    <Rebel_Story_Name>Story_Plots_R.xml</Rebel_Story_Name>\n" +
            "    <Empire_Story_Name>Story_Plots_E.xml</Empire_Story_Name>\n")));
    }

    [Fact]
    public void GenericTag_attaching_several_distinct_factions_emits_no_fact()
    {
        Assert.Empty(Facts(Campaign(
            "    <Story_Name>Rebel, Story_Plots_R.xml, Hutts, Story_Plots_H.xml</Story_Name>\n")));
    }

    [Fact]
    public void Same_faction_attached_by_both_forms_to_the_same_file_is_redundant()
    {
        var facts = Facts(Campaign(
            "    <Rebel_Story_Name>Conquests\\Story_Plots_R.xml</Rebel_Story_Name>\n" +
            "    <Story_Name>Rebel, Conquests\\Story_Plots_R.xml</Story_Name>\n"));

        Assert.Equal(2, facts.Count);
        Assert.All(facts, f =>
        {
            Assert.Equal(CampaignStoryAttachmentProblem.RedundantAttachment, f.Problem);
            Assert.Equal("Rebel", f.Faction);
        });
        // Each occurrence points at the other one.
        Assert.Equal([4], facts[0].OtherLines);
        Assert.Equal([3], facts[1].OtherLines);
    }

    [Fact]
    public void Same_faction_attached_to_different_files_conflicts()
    {
        var facts = Facts(Campaign(
            "    <Rebel_Story_Name>Story_Plots_A.xml</Rebel_Story_Name>\n" +
            "    <Story_Name>Rebel, Story_Plots_B.xml</Story_Name>\n"));

        Assert.Equal(2, facts.Count);
        Assert.All(facts, f =>
        {
            Assert.Equal(CampaignStoryAttachmentProblem.ConflictingAttachment, f.Problem);
            Assert.Equal("Rebel", f.Faction);
            Assert.Equal(["Story_Plots_A.xml", "Story_Plots_B.xml"], f.PlotFiles);
        });
    }

    [Fact]
    public void Repeated_generic_tags_conflicting_on_one_faction_are_correlated()
    {
        var facts = Facts(Campaign(
            "    <Story_Name>Rebel, Story_Plots_A.xml</Story_Name>\n" +
            "    <Story_Name>rebel, Story_Plots_B.xml</Story_Name>\n"));

        Assert.Equal(2, facts.Count);
        Assert.All(facts, f =>
            Assert.Equal(CampaignStoryAttachmentProblem.ConflictingAttachment, f.Problem));
    }

    [Fact]
    public void One_generic_tag_attaching_a_faction_twice_is_correlated()
    {
        var facts = Facts(Campaign(
            "    <Story_Name>Rebel, Story_Plots_A.xml, Rebel, Story_Plots_B.xml</Story_Name>\n"));

        Assert.Equal(2, facts.Count);
        Assert.All(facts, f =>
            Assert.Equal(CampaignStoryAttachmentProblem.ConflictingAttachment, f.Problem));
    }

    [Fact]
    public void Empty_slots_do_not_shift_the_generic_tag_pairing()
    {
        // The trailing comma the real data ships (and any stray double comma) drops out before
        // pairing - same as StoryNameTagSyntax.ReadPairs, which is what actually loads the chain.
        var facts = Facts(Campaign(
            "    <Rebel_Story_Name>Story_Plots_A.xml</Rebel_Story_Name>\n" +
            "    <Story_Name>Rebel,, Story_Plots_B.xml,</Story_Name>\n"));

        Assert.Equal(2, facts.Count);
        Assert.All(facts, f =>
            Assert.Equal(CampaignStoryAttachmentProblem.ConflictingAttachment, f.Problem));
    }

    [Theory]
    [InlineData("Conquests/Story_Plots_R.xml")]
    [InlineData("DATA\\XML\\Conquests\\Story_Plots_R.xml")]
    [InlineData("conquests\\story_plots_r.xml")]
    public void Plot_files_are_compared_in_the_shared_normal_form(string otherSpelling)
    {
        // Same file written differently is redundancy, not a conflict.
        var facts = Facts(Campaign(
            "    <Rebel_Story_Name>Conquests\\Story_Plots_R.xml</Rebel_Story_Name>\n" +
            $"    <Story_Name>Rebel, {otherSpelling}</Story_Name>\n"));

        Assert.All(facts, f =>
            Assert.Equal(CampaignStoryAttachmentProblem.RedundantAttachment, f.Problem));
    }

    [Fact]
    public void Mixing_the_two_forms_for_different_factions_is_reported_once()
    {
        var facts = Facts(Campaign(
            "    <Rebel_Story_Name>Story_Plots_R.xml</Rebel_Story_Name>\n" +
            "    <Story_Name>Hutts, Story_Plots_H.xml</Story_Name>\n"));

        var fact = Assert.Single(facts);
        Assert.Equal(CampaignStoryAttachmentProblem.MixedAuthoringForms, fact.Problem);
        Assert.Equal("Test", fact.CampaignName);
    }

    [Fact]
    public void Mixed_forms_are_not_also_reported_when_a_faction_overlaps()
    {
        // The overlap diagnostic is the sharper one; adding a form-mixing hint on top is noise.
        var facts = Facts(Campaign(
            "    <Rebel_Story_Name>Story_Plots_R.xml</Rebel_Story_Name>\n" +
            "    <Story_Name>Rebel, Story_Plots_R.xml, Hutts, Story_Plots_H.xml</Story_Name>\n"));

        Assert.DoesNotContain(facts, f => f.Problem == CampaignStoryAttachmentProblem.MixedAuthoringForms);
    }

    [Fact]
    public void A_malformed_faction_tag_does_not_also_conflict_with_the_generic_tag()
    {
        // The reported case: the shape error is the one actionable diagnostic. Correlating the
        // malformed value on top would report a conflict against a filename the author never wrote.
        var facts = Facts(Campaign(
            "    <Rebel_Story_Name>test, Conquests\\Story_Plots_GCMenu.xml</Rebel_Story_Name>\n" +
            "    <Story_Name>Rebel, Conquests\\Story_Plots_GCMenu.xml</Story_Name>\n"));

        var fact = Assert.Single(facts);
        Assert.Equal(CampaignStoryAttachmentProblem.TupleInFactionSpecificTag, fact.Problem);
    }

    [Fact]
    public void Attachments_are_not_correlated_across_campaigns()
    {
        const string xml = "<Campaigns>\n" +
                           "  <Campaign Name=\"A\">\n" +
                           "    <Rebel_Story_Name>Story_Plots_A.xml</Rebel_Story_Name>\n" +
                           "  </Campaign>\n" +
                           "  <Campaign Name=\"B\">\n" +
                           "    <Rebel_Story_Name>Story_Plots_B.xml</Rebel_Story_Name>\n" +
                           "  </Campaign>\n" +
                           "</Campaigns>\n";

        Assert.Empty(Facts(xml));
    }

    [Fact]
    public void Correlation_fact_anchors_on_the_offending_plot_file_token()
    {
        const string prefix = "    <Story_Name>Rebel, ";
        var facts = Facts(Campaign(
            "    <Rebel_Story_Name>Story_Plots_A.xml</Rebel_Story_Name>\n" +
            prefix + "Story_Plots_B.xml</Story_Name>\n"));

        var generic = facts.Single(f => f.TagName == "Story_Name");
        Assert.Equal(3, generic.Line);
        Assert.Equal(prefix.Length, generic.Column);
        Assert.Equal("Story_Plots_B.xml".Length, generic.Length);
    }
}

file sealed class NoFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string fileUri)
    {
    }
}
