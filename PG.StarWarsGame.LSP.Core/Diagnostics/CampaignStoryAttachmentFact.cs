// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     What is wrong with how a <c>&lt;Campaign&gt;</c> attaches plot manifests to factions.
///     Existence of the referenced file is NOT covered here - that is the story chain scan's job.
/// </summary>
public enum CampaignStoryAttachmentProblem
{
    /// <summary>
    ///     A <c>{Faction}_Story_Name</c> value carries a comma. That tag takes exactly one plot
    ///     file; the <c>Faction, PlotFile</c> tuple form belongs in the generic <c>Story_Name</c>.
    /// </summary>
    TupleInFactionSpecificTag,

    /// <summary>
    ///     One faction is attached more than once in the same campaign, every occurrence naming the
    ///     same plot file. Harmless to the engine, but one of them is dead weight.
    /// </summary>
    RedundantAttachment,

    /// <summary>
    ///     One faction is attached more than once in the same campaign with DIFFERENT plot files.
    ///     Which manifest the faction ends up with is not something the author can read off the file.
    /// </summary>
    ConflictingAttachment,

    /// <summary>
    ///     The campaign uses both authoring forms (a <c>{Faction}_Story_Name</c> tag and the generic
    ///     <c>Story_Name</c>) for different factions. Legal - only the generic form can attach a
    ///     non-major faction - but worth surfacing so the split is deliberate.
    /// </summary>
    MixedAuthoringForms
}

/// <summary>
///     A defect in one <c>&lt;Campaign&gt;</c> element's story attachments, anchored to the value
///     that caused it. <see cref="Faction" /> is the faction the offending attachment names (empty
///     for <see cref="CampaignStoryAttachmentProblem.MixedAuthoringForms" />, which is a property of
///     the campaign rather than of one attachment).
/// </summary>
public sealed record CampaignStoryAttachmentFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    CampaignStoryAttachmentProblem Problem,
    string TagName,
    string Faction,
    string Value
) : XmlFact(DocumentUri, Line, Column, Length)
{
    /// <summary>
    ///     Printable (1-based) lines of the OTHER occurrences attaching the same faction, in
    ///     document order. Empty when the problem concerns a single occurrence.
    /// </summary>
    public IReadOnlyList<int> OtherLines { get; init; } = [];

    /// <summary>
    ///     The distinct plot files the faction is attached to, as written, in document order.
    ///     Carries more than one entry only for
    ///     <see cref="CampaignStoryAttachmentProblem.ConflictingAttachment" />.
    /// </summary>
    public IReadOnlyList<string> PlotFiles { get; init; } = [];

    /// <summary>The campaign's <c>Name</c> attribute, for message context. Empty when unnamed.</summary>
    public string CampaignName { get; init; } = string.Empty;
}
