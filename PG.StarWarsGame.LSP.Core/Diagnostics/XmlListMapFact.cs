// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>One item of a <see cref="XmlValueType.ListMap" /> occurrence, where it sits in the document.</summary>
public sealed record ListMapItem(string Text, int Line, int Column);

/// <summary>One occurrence of a <see cref="XmlValueType.ListMap" /> tag: its opening tag and its items.</summary>
public sealed record ListMapOccurrence(int Line, int Column, int Length, IReadOnlyList<ListMapItem> Items);

/// <summary>
///     Observation: every occurrence of one <see cref="XmlValueType.ListMap" /> tag on one object, in
///     document order. Positioned on the first occurrence's opening tag.
/// </summary>
/// <remarks>
///     One fact for all of them rather than one per occurrence, because the engine reads them as
///     one list: outside a variant a repeat appends, so whether it starts with a key depends on what
///     came before; inside one each repeat clears the list, so all but the last are dead. Telling a
///     key from an item needs the index, which the producer does not have, so the items arrive
///     unclassified.
/// </remarks>
/// <param name="IsVariant">Whether the object derives from another - its repeats then replace rather than append.</param>
public sealed record XmlListMapFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    XmlTagDefinition Tag,
    bool IsVariant,
    IReadOnlyList<ListMapOccurrence> Occurrences) : XmlFact(DocumentUri, Line, Column, Length);