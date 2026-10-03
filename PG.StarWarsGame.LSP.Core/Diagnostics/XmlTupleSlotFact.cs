// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     One typed item of a slotted tuple value, positioned on the item itself.
/// </summary>
/// <remarks>
///     A fact of its own rather than a check hung off <see cref="XmlTagValueFact" />: every tag that
///     declares slots today also uses <c>mode: replace</c>, which discards the default handlers of
///     the tag value's fact. Only enum and model items get one - an object item is a parser
///     reference, and an untyped item is checked for nothing.
/// </remarks>
public sealed record XmlTupleSlotFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    XmlTagDefinition Tag,
    TupleSlotDefinition Slot,
    string Value) : XmlFact(DocumentUri, Line, Column, Length);