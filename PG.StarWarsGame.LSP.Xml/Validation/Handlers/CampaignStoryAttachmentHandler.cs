// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Renders the campaign story-attachment defects found by
///     <see cref="CrossTagRules.CampaignStoryAttachmentRule" />.
/// </summary>
public sealed class CampaignStoryAttachmentHandler : XmlDiagnosticsHandler<CampaignStoryAttachmentFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.CampaignStoryAttachment;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        CampaignStoryAttachmentFact fact, DiagnosticsContext ctx)
    {
        return fact.Problem switch
        {
            CampaignStoryAttachmentProblem.TupleInFactionSpecificTag => [TupleInFactionSpecificTag(fact, ctx.Messages)],
            CampaignStoryAttachmentProblem.RedundantAttachment => [RedundantAttachment(fact, ctx.Messages)],
            CampaignStoryAttachmentProblem.ConflictingAttachment => [ConflictingAttachment(fact, ctx.Messages)],
            CampaignStoryAttachmentProblem.MixedAuthoringForms => [MixedAuthoringForms(fact, ctx.Messages)],
            _ => []
        };
    }

    // Warning, not Error: the engine merges every occurrence into one (faction, plot) list, so
    // attaching the same manifest twice loads it once and works - it is just dead authoring.
    private static XmlDiagnosticResult RedundantAttachment(CampaignStoryAttachmentFact fact, IMessageCatalog messages)
    {
        return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Warning,
            messages.Format("CampaignStoryAttachment_TheFactionIsAttachedTo", fact.Faction, fact.Value,
                InCampaign(fact, messages), AlsoAt(fact.OtherLines, messages)),
            Id: DiagnosticIds.CampaignStoryRedundantAttachment);
    }

    // Error: the engine merges both attachments, so which manifest the faction actually runs is
    // not something the author can read off the file - and only one of them can be intended.
    private static XmlDiagnosticResult ConflictingAttachment(CampaignStoryAttachmentFact fact, IMessageCatalog messages)
    {
        return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Error,
            messages.Format("CampaignStoryAttachment_TheFactionIsAttachedTo2", fact.Faction, fact.PlotFiles.Count,
                InCampaign(fact, messages), string.Join(", ", fact.PlotFiles.Select(f => $"'{f}'")),
                AlsoAt(fact.OtherLines, messages)), Id: DiagnosticIds.CampaignStoryConflictingAttachment);
    }

    // Information: legal and sometimes unavoidable - only the generic tag can attach a non-major
    // faction - so this states the split rather than asking for a change.
    private static XmlDiagnosticResult MixedAuthoringForms(CampaignStoryAttachmentFact fact, IMessageCatalog messages)
    {
        return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Information,
            messages.Format("CampaignStoryAttachment_CampaignAttachesPlotsThroughBoth", Named(fact, messages),
                StoryNameTagSyntax.GenericTag),
            Id: DiagnosticIds.CampaignStoryMixedAuthoringForms);
    }

    private static string InCampaign(CampaignStoryAttachmentFact fact, IMessageCatalog messages)
    {
        return fact.CampaignName.Length == 0
            ? string.Empty
            : messages.Format("CampaignStoryAttachment_InCampaign", fact.CampaignName);
    }

    private static string Named(CampaignStoryAttachmentFact fact, IMessageCatalog messages)
    {
        return fact.CampaignName.Length == 0 ? string.Empty : $" '{fact.CampaignName}'";
    }

    private static string AlsoAt(IReadOnlyList<int> otherLines, IMessageCatalog messages)
    {
        return otherLines.Count switch
        {
            0 => string.Empty,
            1 => messages.Format("CampaignStoryAttachment_AlsoAtLine", otherLines[0]),
            _ => messages.Format("CampaignStoryAttachment_AlsoAtLines", string.Join(", ", otherLines))
        };
    }

    private static XmlDiagnosticResult TupleInFactionSpecificTag(CampaignStoryAttachmentFact fact,
        IMessageCatalog messages)
    {
        return new XmlDiagnosticResult(
            XmlDiagnosticSeverity.Error,
            messages.Format("CampaignStoryAttachment_TakesASinglePlotManifest", fact.TagName,
                StoryNameTagSyntax.GenericTag, fact.Value, StoryNameTagSyntax.GenericTag),
            SuggestedFix: SingleRemainingPlotFile(fact.Value), Id: DiagnosticIds.CampaignStoryTupleInFactionTag);
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