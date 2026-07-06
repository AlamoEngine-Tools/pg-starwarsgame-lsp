// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a singleton tag appears more than once under the same parent.
///     <see cref="XmlFact.Line" /> is the line of the current occurrence;
///     <see cref="OtherLines" /> lists the other occurrences (1-based, as HAP reports them).
/// </summary>
public sealed record XmlDuplicateTagFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    XmlTagDefinition Tag,
    IReadOnlyList<int> OtherLines,
    // True for the occurrence the game engine actually uses: it reads objects top to bottom and
    // the LAST duplicate wins. Earlier occurrences are dead weight (greyed out as Unnecessary).
    bool IsLastOccurrence = false,
    // Whole-element span so the grey-out covers the entire node, not just the opening tag.
    int? EndLine = null,
    int? EndColumn = null) : XmlFact(DocumentUri, Line, Column, Length, EndLine, EndColumn);