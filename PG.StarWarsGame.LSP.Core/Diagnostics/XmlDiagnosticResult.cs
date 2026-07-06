// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Lightweight diagnostic observation returned by a handler. Position comes from the originating
///     <see cref="XmlFact" />; override fields are used only when a handler must report at a different
///     location than the fact (e.g. each occurrence in a duplicate-tag set).
///     <see cref="OverrideEndLine" />/<see cref="OverrideEndColumn" /> mirror
///     <see cref="XmlFact.EndLine" />/<see cref="XmlFact.EndColumn" /> — set them when a handler needs
///     a cross-line range different from what the fact itself carries; otherwise the fact's own
///     EndLine/EndColumn (if any) are used.
/// </summary>
public record XmlDiagnosticResult(
    XmlDiagnosticSeverity Severity,
    string Message,
    int? OverrideLine = null,
    int? OverrideColumn = null,
    int? OverrideLength = null,
    string? SuggestedFix = null,
    string? CreateLocalisationKey = null,
    string? SquadronSyncJson = null,
    IReadOnlyList<XmlDiagnosticTag>? Tags = null,
    bool RemoveRedundantOverride = false,
    int? OverrideEndLine = null,
    int? OverrideEndColumn = null,
    // Navigable companion positions rendered as LSP DiagnosticRelatedInformation (clickable in
    // the editor) - e.g. the OTHER definitions of a duplicate symbol. Only editor-openable
    // file:// URIs belong here.
    IReadOnlyList<XmlRelatedLocation>? RelatedLocations = null,
    // Marks the diagnostic as eligible for the "remove earlier duplicate occurrences" quick fix
    // (duplicate singleton tags within one object; the game keeps the last occurrence).
    bool OfferRemoveEarlierDuplicates = false);

/// <summary>A navigable location referenced by a diagnostic (LSP related information).</summary>
public sealed record XmlRelatedLocation(string Uri, int Line, int? Column, string Message);