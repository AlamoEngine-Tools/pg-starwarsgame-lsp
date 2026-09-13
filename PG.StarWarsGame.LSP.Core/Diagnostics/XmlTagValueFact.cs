// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>Observation: a tag with a non-empty value was found at the given position.</summary>
/// <param name="OwningType">
///     The schema type that declares this tag on this element, where it is known. Needed because a
///     tag name alone does not identify a rule: the same name under two owners can carry different
///     bounds and different engine repairs. Null when the walk had no owning type in hand, which a
///     consumer must read as "cannot tell" rather than as a default.
/// </param>
public sealed record XmlTagValueFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    XmlTagDefinition Tag,
    string RawValue,
    string? OwningType = null) : XmlFact(DocumentUri, Line, Column, Length);