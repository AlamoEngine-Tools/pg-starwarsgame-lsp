// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Writer;

namespace PG.StarWarsGame.LSP.Story.Tests.Writer;

/// <summary>
///     Round-trip fixtures for campaign story-name attach/detach across both authoring forms
///     (faction-specific <c>Rebel_Story_Name</c> and the generic additive <c>Story_Name</c> tuple
///     list). Every test applies the edits and asserts the EXACT resulting text - untouched lines
///     stay byte-identical.
/// </summary>
public sealed class StoryManifestWriterTest
{
    // EaWX-style campaign: generic Story_Name tag with a subdir plot path and trailing comma.
    private const string GenericCampaign =
        "<?xml version=\"1.0\"?>\n" +
        "<Campaigns>\n" +
        "\t<Campaign Name=\"GC\">\n" +
        "\t\t<Sort_Order>001</Sort_Order>\n" +
        "\t\t<Story_Name>Independent_Forces, Conquests\\Loader\\Story_Plots_Loader.xml,</Story_Name>\n" +
        "\t</Campaign>\n" +
        "</Campaigns>\n";

    private static string Apply(string text, IReadOnlyList<StoryTextEdit> edits)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                lineStarts.Add(i + 1);

        int Offset(int line, int column)
        {
            return line >= lineStarts.Count ? text.Length : Math.Min(lineStarts[line] + column, text.Length);
        }

        var result = text;
        foreach (var edit in edits.OrderByDescending(e => Offset(e.Range.StartLine, e.Range.StartColumn)))
        {
            var start = Offset(edit.Range.StartLine, edit.Range.StartColumn);
            var end = Offset(edit.Range.EndLine, edit.Range.EndColumn);
            result = result[..start] + edit.NewText + result[end..];
        }

        return result;
    }

    // ── Detach ───────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_GenericTag_SolePair_DropsWholeTag_MatchingNormalized()
    {
        // The model hands back the canonical forward-slash path; the tag text has backslashes.
        var edits = StoryManifestWriter.RemoveCampaignStoryName(
            GenericCampaign, "GC", "Conquests/Loader/Story_Plots_Loader.xml");

        Assert.Equal(
            GenericCampaign.Replace(
                "\t\t<Story_Name>Independent_Forces, Conquests\\Loader\\Story_Plots_Loader.xml,</Story_Name>\n",
                ""),
            Apply(GenericCampaign, edits));
    }

    [Fact]
    public void Remove_GenericTupleList_SnipsOnlyMatchedPair_KeepsOthers()
    {
        var fixture =
            "<Campaigns>\n" +
            "\t<Campaign Name=\"GC\">\n" +
            "\t\t<Story_Name>Rebel, R.xml, Empire, E.xml, Independent, I.xml,</Story_Name>\n" +
            "\t</Campaign>\n" +
            "</Campaigns>\n";

        var edits = StoryManifestWriter.RemoveCampaignStoryName(fixture, "GC", "E.xml");

        Assert.Equal(
            fixture.Replace(
                "<Story_Name>Rebel, R.xml, Empire, E.xml, Independent, I.xml,</Story_Name>",
                "<Story_Name>Rebel, R.xml, Independent, I.xml</Story_Name>"),
            Apply(fixture, edits));
    }

    [Fact]
    public void Remove_FactionSpecificTag_SubdirPath_MatchesNormalized()
    {
        var fixture =
            "<Campaigns>\n" +
            "\t<Campaign Name=\"GC\">\n" +
            "\t\t<Rebel_Story_Name>Conquests\\Loader\\Story_Plots_R.xml</Rebel_Story_Name>\n" +
            "\t</Campaign>\n" +
            "</Campaigns>\n";

        var edits = StoryManifestWriter.RemoveCampaignStoryName(
            fixture, "GC", "Conquests/Loader/Story_Plots_R.xml");

        Assert.Equal(
            fixture.Replace(
                "\t\t<Rebel_Story_Name>Conquests\\Loader\\Story_Plots_R.xml</Rebel_Story_Name>\n", ""),
            Apply(fixture, edits));
    }

    [Fact]
    public void Remove_UnattachedManifest_IsNoOp()
    {
        var edits = StoryManifestWriter.RemoveCampaignStoryName(GenericCampaign, "GC", "Not_Here.xml");
        Assert.Empty(edits);
    }

    // ── Attach ───────────────────────────────────────────────────────────────

    [Fact]
    public void Add_NonMajorFaction_WritesGenericStoryNameTag()
    {
        var fixture =
            "<Campaigns>\n" +
            "\t<Campaign Name=\"GC\">\n" +
            "\t\t<Sort_Order>001</Sort_Order>\n" +
            "\t</Campaign>\n" +
            "</Campaigns>\n";

        var edits = StoryManifestWriter.AddCampaignStoryName(
            fixture, "GC", "Independent_Forces", "Story_Plots_Loader.xml");

        // No existing story-name tag → anchored right below the campaign open tag.
        Assert.Equal(
            fixture.Replace(
                "\t<Campaign Name=\"GC\">\n",
                "\t<Campaign Name=\"GC\">\n" +
                "\t\t<Story_Name>Independent_Forces, Story_Plots_Loader.xml</Story_Name>\n"),
            Apply(fixture, edits));
    }

    [Fact]
    public void Add_MajorFaction_WritesFactionSpecificTag()
    {
        var fixture =
            "<Campaigns>\n" +
            "\t<Campaign Name=\"GC\">\n" +
            "\t\t<Sort_Order>001</Sort_Order>\n" +
            "\t</Campaign>\n" +
            "</Campaigns>\n";

        var edits = StoryManifestWriter.AddCampaignStoryName(
            fixture, "GC", "Rebel", "Story_Plots_R.xml");

        // No existing story-name tag → anchored right below the campaign open tag.
        Assert.Equal(
            fixture.Replace(
                "\t<Campaign Name=\"GC\">\n",
                "\t<Campaign Name=\"GC\">\n" +
                "\t\t<Rebel_Story_Name>Story_Plots_R.xml</Rebel_Story_Name>\n"),
            Apply(fixture, edits));
    }

    // ── Full round-trip ──────────────────────────────────────────────────────

    [Fact]
    public void AttachThenDetach_NonMajorFaction_ReturnsToOriginal()
    {
        var fixture =
            "<Campaigns>\n" +
            "\t<Campaign Name=\"GC\">\n" +
            "\t\t<Sort_Order>001</Sort_Order>\n" +
            "\t</Campaign>\n" +
            "</Campaigns>\n";

        var attached = Apply(fixture,
            StoryManifestWriter.AddCampaignStoryName(fixture, "GC", "Hutt_Cartels", "Story_Plots_H.xml"));
        var detached = Apply(attached,
            StoryManifestWriter.RemoveCampaignStoryName(attached, "GC", "Story_Plots_H.xml"));

        Assert.Equal(fixture, detached);
    }
}
