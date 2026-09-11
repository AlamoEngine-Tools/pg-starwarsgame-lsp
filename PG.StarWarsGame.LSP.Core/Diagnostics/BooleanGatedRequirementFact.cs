// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a flag is switched on without the tag the engine requires alongside it.
/// </summary>
/// <remarks>
///     <para>
///         Six of these, each stated in one ability class's <c>Validate_Data</c> as "If you set A
///         to true you must also set B". The engine does not refuse the object - it REPAIRS it,
///         turning the missing flag on or forcing the missing duration to a default - so the
///         ability runs under settings the author never wrote and nothing on screen says so.
///     </para>
///     <para>
///         Neither tag is wrong alone, which is what makes this a cross-tag rule: the detail flag
///         is fine when off, and the summary flag is fine when nothing needs it.
///     </para>
/// </remarks>
/// <param name="GateTag">The flag whose being on creates the requirement.</param>
/// <param name="RequiredTag">The tag that must then be set.</param>
/// <param name="Requirement">
///     What the required tag must be, as a sentence fragment - "set to true", "a time greater than
///     zero".
/// </param>
/// <param name="Repair">What the engine silently does instead, for the second half of the message.</param>
public sealed record BooleanGatedRequirementFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string GateTag,
    string RequiredTag,
    string Requirement,
    string Repair) : XmlFact(DocumentUri, Line, Column, Length);
