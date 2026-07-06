// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Base for all XML document observations. Carries position info; makes no judgement about severity.
///     <paramref name="EndLine" />/<paramref name="EndColumn" /> are optional and only set by facts
///     that represent a genuine cross-line span (e.g. a whole XML element) — most facts describe a
///     single-line token and leave them null, in which case the diagnostic range is
///     <c>(Line, Column)</c> to <c>(Line, Column + Length)</c> as before.
/// </summary>
public abstract record XmlFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    int? EndLine = null,
    int? EndColumn = null);