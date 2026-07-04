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
    int? OverrideEndColumn = null);