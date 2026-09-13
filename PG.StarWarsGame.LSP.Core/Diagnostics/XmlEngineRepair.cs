// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     One replacement in the document, in the fact's own coordinates.
/// </summary>
/// <param name="Line">0-based line.</param>
/// <param name="Column">0-based column of the first character replaced.</param>
/// <param name="Length">Characters replaced. Zero would be an insertion, which nothing emits yet.</param>
/// <param name="NewText">What goes there instead.</param>
public sealed record XmlDiagnosticEdit(int Line, int Column, int Length, string NewText);

/// <summary>
///     The correction the ENGINE applies to this object at load, offered as a quick fix.
/// </summary>
/// <remarks>
///     <para>
///         Distinct from <c>SuggestedFix</c>, which replaces the diagnostic's own range and covers
///         every case where the offending value is the thing being reported. This carries explicit
///         edits because the repair may land on a DIFFERENT tag than the one reported, or on two of
///         them: the automatic-style rule reports an ability and turns off a child flag, and the
///         respawn rule exchanges a pair of values.
///     </para>
///     <para>
///         The edits are computed where the parsed nodes are - in the rule - rather than by
///         re-parsing in the code-action layer, so the positions are the same ones the diagnostic
///         was built from and cannot drift from them.
///     </para>
///     <para>
///         Every repair here is read out of the binary: the assignment the engine performs after it
///         complains. Where it only complains, there is no repair and none is offered.
///     </para>
/// </remarks>
/// <param name="Title">What the lightbulb entry says, in the engine's terms.</param>
/// <param name="Edits">Applied together as one workspace edit; order is the document's.</param>
public sealed record XmlEngineRepair(string Title, IReadOnlyList<XmlDiagnosticEdit> Edits);
