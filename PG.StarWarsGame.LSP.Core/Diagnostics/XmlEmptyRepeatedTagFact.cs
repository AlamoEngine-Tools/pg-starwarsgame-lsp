// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a tag occurrence the game maps no value from - empty, or exactly <c>TBD</c> -
///     where a value is already set, so it clears nothing. <see cref="KeptValue" /> is what stays;
///     <see cref="KeptLine" /> is its line in this document (1-based, as HAP reports lines), or
///     null when the value is inherited from the variant base.
/// </summary>
public sealed record XmlEmptyRepeatedTagFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string TagName,
    string KeptValue,
    int? KeptLine,
    // Whole-element span so the grey-out covers the entire dead node, not just the opening tag.
    int? EndLine = null,
    int? EndColumn = null) : XmlFact(DocumentUri, Line, Column, Length, EndLine, EndColumn);