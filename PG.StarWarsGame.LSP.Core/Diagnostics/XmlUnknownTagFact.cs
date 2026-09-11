// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: an element sits where a tag belongs and the schema has no tag by that name at
///     all.
/// </summary>
/// <remarks>
///     <para>
///         The engine's XML parsers are table-driven: a row per tag, looked up by name, and
///         anything that misses is read and dropped. Most classes log an "Unprocessed entry"
///         warning about it, but that reaches a log file nobody opens, so a typo behaves exactly
///         like a value the author never wrote - the unit keeps the default and nothing says why.
///     </para>
///     <para>
///         Globally unknown, not unknown-for-this-owner. <c>XmlObjectTagResolver</c> falls back to a
///         flat lookup across every type, so a tag that exists anywhere resolves everywhere: a real
///         tag on the wrong element is invisible to this fact and needs a rule of its own.
///     </para>
/// </remarks>
/// <param name="TagName">The element as authored, with its original casing.</param>
/// <param name="OwnerElement">The object element the tag was written under, e.g. <c>SpaceUnit</c>.</param>
/// <param name="Suggestion">
///     The known tag this is most likely a misspelling of, or null when nothing is close enough.
/// </param>
public sealed record XmlUnknownTagFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string TagName,
    string OwnerElement,
    string? Suggestion) : XmlFact(DocumentUri, Line, Column, Length);
