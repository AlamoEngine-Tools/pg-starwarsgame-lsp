// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Validates how one <c>&lt;Campaign&gt;</c> attaches plot manifests to factions, across both
///     authoring forms (<c>{Faction}_Story_Name</c> and the generic <c>Story_Name</c> tuple list -
///     see <see cref="StoryNameTagSyntax" />). The engine merges every occurrence of both into one
///     (faction, plot) list, so correlating them is only meaningful per campaign element.
///     <para>
///         Document-local on purpose. The only other place these tags are looked at is the story
///         chain scan, which runs from <c>WorkspaceIndexer.PreScanMetafiles</c> at startup and on
///         project reload only - so nothing it finds ever appears while the campaign file is being
///         edited. Everything decidable from the campaign element alone belongs here instead.
///     </para>
///     Whether a referenced plot file EXISTS stays with the chain scan; this rule never touches the
///     file system.
/// </summary>
public sealed class CampaignStoryAttachmentRule : IXmlCrossTagRule
{
    private const string CampaignElement = "campaign";

    public IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        // HAP lowercases element names. Story-name tags outside a <Campaign> attach nothing, so
        // correlating them would be meaningless - malformed nesting is the structural validator's.
        if (!objectNode.Name.Equals(CampaignElement, StringComparison.OrdinalIgnoreCase))
            return [];

        var facts = new List<XmlFact>();
        var attachments = new List<Attachment>();

        foreach (var (tagName, nodes) in childrenByName)
        {
            if (!StoryNameTagSyntax.IsStoryNameTag(tagName)) continue;
            foreach (var node in nodes)
                if (StoryNameTagSyntax.IsFactionSpecificTag(tagName))
                    ReadFactionSpecificTag(node, tagName, documentUri, lineIndex, facts, attachments);
                else
                    ReadGenericTag(node, lineIndex, attachments);
        }

        // Dictionary order is not document order, and neither is stable across runs; diagnostics
        // must be.
        attachments.Sort((a, b) => a.Line != b.Line ? a.Line - b.Line : a.Column - b.Column);

        var campaignName = objectNode.GetAttributeValue("Name", string.Empty).Trim();
        var overlapping = AddFactionOverlapFacts(attachments, campaignName, documentUri, facts);
        if (!overlapping)
            AddMixedFormsFact(attachments, campaignName, documentUri, facts);

        return facts;
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     A <c>{Faction}_Story_Name</c> value is one plot file, and a plot file path never contains
    ///     a comma - so a comma means the author pasted the generic tag's
    ///     <c>Faction, PlotFile</c> tuple into the wrong tag. Left alone, the whole string is taken
    ///     as a filename and the mistake only ever surfaces as a misleading "file does not exist".
    ///     Such a tag contributes no attachment: correlating a value the author never meant as a
    ///     filename would report a second, phantom problem on top of the real one.
    /// </summary>
    private static void ReadFactionSpecificTag(HtmlNode node, string tagName, string documentUri,
        LineOffsetIndex lineIndex, List<XmlFact> facts, List<Attachment> attachments)
    {
        var value = node.InnerText.Trim();
        if (value.Length == 0) return;

        // HAP lowercased the name; rebuild the schema's casing so messages read as authored.
        var faction = StoryNameTagSyntax.FactionOf(tagName)!;
        var (line, column, length) = XmlUtility.GetValuePosition(node, lineIndex);

        if (value.Contains(','))
        {
            facts.Add(new CampaignStoryAttachmentFact(
                documentUri, line, column, length,
                CampaignStoryAttachmentProblem.TupleInFactionSpecificTag,
                StoryNameTagSyntax.FactionTagFor(faction), faction, value));
            return;
        }

        attachments.Add(new Attachment(faction, value, StoryNameTagSyntax.FactionTagFor(faction),
            FactionSpecific: true, line, column, length, XmlUtility.GetPrintableLine(node)));
    }

    /// <summary>
    ///     The generic tag's flat <c>Faction, PlotFile[, Faction, PlotFile ...]</c> list. Each
    ///     attachment anchors on its own plot-file token, so a campaign with several pairs in one
    ///     tag highlights the offending pair rather than the whole line. A dangling faction with no
    ///     plot file attaches nothing and is left to the tag's own value validation.
    ///     Pairing alternates over the NON-EMPTY tokens, exactly as
    ///     <see cref="StoryNameTagSyntax.ReadPairs" /> does, so what this rule correlates and what
    ///     the story chain scan loads can never drift apart.
    /// </summary>
    private static void ReadGenericTag(HtmlNode node, LineOffsetIndex lineIndex, List<Attachment> attachments)
    {
        string? faction = null;

        foreach (var (token, offset) in XmlUtility.SplitCommaWithOffsets(node.InnerText))
            if (faction is null)
            {
                faction = token;
            }
            else
            {
                var (line, column, length) =
                    XmlUtility.GetInnerOffsetValuePosition(node, offset, token.Length, lineIndex);
                attachments.Add(new Attachment(faction, token, StoryNameTagSyntax.GenericTag,
                    FactionSpecific: false, line, column, length, XmlUtility.GetPrintableLine(node)));
                faction = null;
            }
    }

    // ── Correlation ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Emits one fact per occurrence for every faction attached more than once, and reports
    ///     whether any faction was. Same plot file everywhere is redundancy; differing files mean
    ///     the file no longer says which manifest the faction gets.
    /// </summary>
    private static bool AddFactionOverlapFacts(List<Attachment> attachments, string campaignName,
        string documentUri, List<XmlFact> facts)
    {
        var overlapping = false;

        foreach (var group in attachments
                     .GroupBy(a => a.Faction, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            overlapping = true;
            var occurrences = group.ToList();
            var distinctFiles = occurrences
                .DistinctBy(a => StoryReferenceTypes.NormalizeRelativePath(a.PlotFile),
                    StringComparer.OrdinalIgnoreCase)
                .Select(a => a.PlotFile)
                .ToList();

            var problem = distinctFiles.Count == 1
                ? CampaignStoryAttachmentProblem.RedundantAttachment
                : CampaignStoryAttachmentProblem.ConflictingAttachment;

            foreach (var occurrence in occurrences)
                facts.Add(new CampaignStoryAttachmentFact(
                    documentUri, occurrence.Line, occurrence.Column, occurrence.Length,
                    problem, occurrence.TagName, occurrence.Faction, occurrence.PlotFile)
                {
                    OtherLines = occurrences.Where(o => o != occurrence)
                        .Select(o => o.PrintableLine).ToList(),
                    PlotFiles = distinctFiles,
                    CampaignName = campaignName
                });
        }

        return overlapping;
    }

    /// <summary>
    ///     Both authoring forms in one campaign, for factions that do not overlap. Only reported
    ///     when no faction overlaps - the overlap facts above are the sharper diagnostic on exactly
    ///     the same tags, and stacking a style hint on top of them is noise. Anchored once, on the
    ///     first attachment in document order.
    /// </summary>
    private static void AddMixedFormsFact(List<Attachment> attachments, string campaignName,
        string documentUri, List<XmlFact> facts)
    {
        if (!attachments.Any(a => a.FactionSpecific) || !attachments.Any(a => !a.FactionSpecific))
            return;

        var anchor = attachments[0];
        facts.Add(new CampaignStoryAttachmentFact(
            documentUri, anchor.Line, anchor.Column, anchor.Length,
            CampaignStoryAttachmentProblem.MixedAuthoringForms,
            anchor.TagName, string.Empty, anchor.PlotFile)
        {
            CampaignName = campaignName
        });
    }

    /// <summary>One (faction, plot file) attachment, with the source span of its plot-file token.</summary>
    private sealed record Attachment(
        string Faction,
        string PlotFile,
        string TagName,
        bool FactionSpecific,
        int Line,
        int Column,
        int Length,
        int PrintableLine);
}
