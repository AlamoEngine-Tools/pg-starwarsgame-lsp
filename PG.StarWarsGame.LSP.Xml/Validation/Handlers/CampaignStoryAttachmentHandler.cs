// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Renders the campaign story-attachment defects found by
///     <see cref="CrossTagRules.CampaignStoryAttachmentRule" />.
/// </summary>
public sealed class CampaignStoryAttachmentHandler : XmlDiagnosticsHandler<CampaignStoryAttachmentFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(
        CampaignStoryAttachmentFact fact, DiagnosticsContext ctx)
    {
        return fact.Problem switch
        {
            CampaignStoryAttachmentProblem.TupleInFactionSpecificTag => [TupleInFactionSpecificTag(fact)],
            CampaignStoryAttachmentProblem.RedundantAttachment => [RedundantAttachment(fact)],
            CampaignStoryAttachmentProblem.ConflictingAttachment => [ConflictingAttachment(fact)],
            CampaignStoryAttachmentProblem.MixedAuthoringForms => [MixedAuthoringForms(fact)],
            _ => []
        };
    }

    // Warning, not Error: the engine merges every occurrence into one (faction, plot) list, so
    // attaching the same manifest twice loads it once and works - it is just dead authoring.
    private static XmlDiagnosticResult RedundantAttachment(CampaignStoryAttachmentFact fact)
    {
        return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Warning,
            $"The {fact.Faction} faction is attached to '{fact.Value}' more than once{InCampaign(fact)}. " +
            $"Only one attachment is needed.{AlsoAt(fact.OtherLines)}");
    }

    // Error: the engine merges both attachments, so which manifest the faction actually runs is
    // not something the author can read off the file - and only one of them can be intended.
    private static XmlDiagnosticResult ConflictingAttachment(CampaignStoryAttachmentFact fact)
    {
        return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Error,
            $"The {fact.Faction} faction is attached to {fact.PlotFiles.Count} different plot manifests" +
            $"{InCampaign(fact)}: {string.Join(", ", fact.PlotFiles.Select(f => $"'{f}'"))}. " +
            $"Keep one.{AlsoAt(fact.OtherLines)}");
    }

    // Information: legal and sometimes unavoidable - only the generic tag can attach a non-major
    // faction - so this states the split rather than asking for a change.
    private static XmlDiagnosticResult MixedAuthoringForms(CampaignStoryAttachmentFact fact)
    {
        return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Information,
            $"Campaign{Named(fact)} attaches plots through both the faction-specific " +
            $"<Faction_Story_Name> tags and the generic <{StoryNameTagSyntax.GenericTag}> tuple list. " +
            "Both are read by the engine; only the generic form can attach a non-major faction.");
    }

    private static string InCampaign(CampaignStoryAttachmentFact fact)
    {
        return fact.CampaignName.Length == 0 ? string.Empty : $" in campaign '{fact.CampaignName}'";
    }

    private static string Named(CampaignStoryAttachmentFact fact)
    {
        return fact.CampaignName.Length == 0 ? string.Empty : $" '{fact.CampaignName}'";
    }

    private static string AlsoAt(IReadOnlyList<int> otherLines)
    {
        return otherLines.Count switch
        {
            0 => string.Empty,
            1 => $" Also at line {otherLines[0]}.",
            _ => $" Also at lines {string.Join(", ", otherLines)}."
        };
    }

    private static XmlDiagnosticResult TupleInFactionSpecificTag(CampaignStoryAttachmentFact fact)
    {
        return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Error,
            $"<{fact.TagName}> takes a single plot manifest file, not a 'Faction, PlotFile' tuple - " +
            $"the tag already names the faction. Use <{StoryNameTagSyntax.GenericTag}>{fact.Value}" +
            $"</{StoryNameTagSyntax.GenericTag}> for the generic form, or drop the leading token.",
            SuggestedFix: SingleRemainingPlotFile(fact.Value));
    }

    /// <summary>
    ///     The plot file to keep when the stray faction token is stripped. Only a plain two-token
    ///     "Faction, PlotFile" value has one unambiguous answer; a longer tuple list means two or
    ///     more attachments were crammed into a single-file tag, and no value rewrite preserves that.
    /// </summary>
    private static string? SingleRemainingPlotFile(string value)
    {
        var tokens = value.Split(',', StringSplitOptions.TrimEntries);
        if (tokens.Length != 2) return null;
        return tokens[1].Length == 0 ? null : tokens[1];
    }
}
