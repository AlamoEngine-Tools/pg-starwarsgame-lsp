// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>A story graph-level finding anchored in one document (0-based positions).</summary>
public sealed record StoryGraphDiagnostic(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Message,
    XmlDiagnosticSeverity Severity);

/// <summary>
///     Supplies campaign-model diagnostics (dangling prereqs, cycles, ambiguity, unreachable
///     events, …) for a document. Implemented server-side over the story model service; consumed
///     by the XML diagnostics pipeline alongside the per-document facts.
/// </summary>
public interface IStoryGraphDiagnosticsSource
{
    IReadOnlyList<StoryGraphDiagnostic> GetForDocument(string canonicalUri);
}
