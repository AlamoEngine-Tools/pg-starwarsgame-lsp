// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>What went wrong while following the campaign → plot manifest → story thread chain.</summary>
public enum StoryChainProblemKind
{
    /// <summary>A <c>*_Story_Name</c> value in a campaign resolves to no file in any layer.</summary>
    UnresolvedStoryName,

    /// <summary>An <c>Active_Plot</c>/<c>Suspended_Plot</c> manifest entry resolves to no file in any layer.</summary>
    UnresolvedPlotEntry,

    /// <summary>A tactical plot reference (STORY_*_TACTICAL / LINK_TACTICAL) resolves to no file in any layer.</summary>
    UnresolvedTacticalReference,

    /// <summary>A referenced plot manifest has no <c>&lt;Story_Mode_Plots&gt;</c> root element.</summary>
    MalformedManifest
}

/// <summary>
///     A defect found while scanning the story chain, anchored to the reference that caused it
///     (positions are 0-based). <see cref="DocumentUri" /> is the canonical URI of the file
///     containing the reference when the scan ran against the workspace; <c>null</c> when the
///     source has no on-disk identity (e.g. a baseline scan).
/// </summary>
public sealed record StoryChainProblem(
    string SourceFile,
    string? DocumentUri,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    StoryChainProblemKind Kind,
    string Reference,
    string Message,
    XmlDiagnosticSeverity Severity);