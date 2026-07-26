// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class CampaignStoryAttachmentHandlerTest
{
    private const string Uri = "file:///Campaigns/Campaigns_Test.xml";

    private static IReadOnlyList<XmlDiagnosticResult> Handle(
        CampaignStoryAttachmentProblem problem, string tagName, string faction, string value)
    {
        var fact = new CampaignStoryAttachmentFact(
            Uri, 2, 25, value.Length, problem, tagName, faction, value);
        return new CampaignStoryAttachmentHandler()
            .Handle(fact, XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
    }

    [Fact]
    public void TupleInFactionSpecificTag_is_an_error_naming_the_generic_tag_as_the_fix()
    {
        var result = Assert.Single(Handle(
            CampaignStoryAttachmentProblem.TupleInFactionSpecificTag,
            "Rebel_Story_Name", "Rebel", "test, Conquests\\Story_Plots_GCMenu.xml"));

        Assert.Equal(XmlDiagnosticSeverity.Error, result.Severity);
        Assert.Contains("Rebel_Story_Name", result.Message);
        Assert.Contains("<Story_Name>", result.Message);
        Assert.Contains("single", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TupleInFactionSpecificTag_suggests_dropping_the_stray_faction_token()
    {
        // The quick fix replaces the VALUE range, so it cannot rename the tag - it can only strip
        // the faction token the tag already implies.
        var result = Assert.Single(Handle(
            CampaignStoryAttachmentProblem.TupleInFactionSpecificTag,
            "Rebel_Story_Name", "Rebel", "test, Conquests\\Story_Plots_GCMenu.xml"));

        Assert.Equal("Conquests\\Story_Plots_GCMenu.xml", result.SuggestedFix);
    }

    [Fact]
    public void TupleInFactionSpecificTag_with_more_than_one_pair_offers_no_fix()
    {
        // Two attachments crammed into a single-file tag: no single value rewrite preserves intent.
        var result = Assert.Single(Handle(
            CampaignStoryAttachmentProblem.TupleInFactionSpecificTag,
            "Rebel_Story_Name", "Rebel", "Rebel, A.xml, Empire, B.xml"));

        Assert.Null(result.SuggestedFix);
    }

    [Fact]
    public void TupleInFactionSpecificTag_with_an_empty_plot_slot_offers_no_fix()
    {
        var result = Assert.Single(Handle(
            CampaignStoryAttachmentProblem.TupleInFactionSpecificTag,
            "Rebel_Story_Name", "Rebel", "Rebel,"));

        Assert.Null(result.SuggestedFix);
    }

    [Fact]
    public void RedundantAttachment_is_a_warning_naming_the_faction_and_the_other_line()
    {
        var fact = new CampaignStoryAttachmentFact(
            Uri, 2, 25, 10, CampaignStoryAttachmentProblem.RedundantAttachment,
            "Story_Name", "Rebel", "Story_Plots_R.xml")
        {
            OtherLines = [3], PlotFiles = ["Story_Plots_R.xml"], CampaignName = "Test"
        };

        var result = Assert.Single(Handle(fact));
        Assert.Equal(XmlDiagnosticSeverity.Warning, result.Severity);
        Assert.Contains("Rebel", result.Message);
        Assert.Contains("Story_Plots_R.xml", result.Message);
        Assert.Contains("line 3", result.Message);
    }

    [Fact]
    public void RedundantAttachment_lists_every_other_line()
    {
        var fact = new CampaignStoryAttachmentFact(
            Uri, 2, 25, 10, CampaignStoryAttachmentProblem.RedundantAttachment,
            "Story_Name", "Rebel", "Story_Plots_R.xml")
        {
            OtherLines = [3, 7], PlotFiles = ["Story_Plots_R.xml"], CampaignName = "Test"
        };

        var result = Assert.Single(Handle(fact));
        Assert.Contains("lines 3, 7", result.Message);
    }

    [Fact]
    public void ConflictingAttachment_is_an_error_listing_the_competing_manifests()
    {
        var fact = new CampaignStoryAttachmentFact(
            Uri, 2, 25, 10, CampaignStoryAttachmentProblem.ConflictingAttachment,
            "Story_Name", "Rebel", "Story_Plots_B.xml")
        {
            OtherLines = [3],
            PlotFiles = ["Story_Plots_A.xml", "Story_Plots_B.xml"],
            CampaignName = "Test"
        };

        var result = Assert.Single(Handle(fact));
        Assert.Equal(XmlDiagnosticSeverity.Error, result.Severity);
        Assert.Contains("Rebel", result.Message);
        Assert.Contains("Story_Plots_A.xml", result.Message);
        Assert.Contains("Story_Plots_B.xml", result.Message);
        Assert.Contains("Test", result.Message);
    }

    [Fact]
    public void MixedAuthoringForms_is_informational()
    {
        var fact = new CampaignStoryAttachmentFact(
            Uri, 2, 25, 10, CampaignStoryAttachmentProblem.MixedAuthoringForms,
            "Rebel_Story_Name", string.Empty, "Story_Plots_R.xml") { CampaignName = "Test" };

        var result = Assert.Single(Handle(fact));
        Assert.Equal(XmlDiagnosticSeverity.Information, result.Severity);
        Assert.Contains("<Story_Name>", result.Message);
        Assert.Contains("Test", result.Message);
        Assert.Null(result.SuggestedFix);
    }

    private static IReadOnlyList<XmlDiagnosticResult> Handle(CampaignStoryAttachmentFact fact)
    {
        return new CampaignStoryAttachmentHandler()
            .Handle(fact, XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
    }
}
