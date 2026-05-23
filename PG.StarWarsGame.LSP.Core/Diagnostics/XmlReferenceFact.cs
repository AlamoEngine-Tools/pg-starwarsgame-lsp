// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a reference to a game object was found at the given position.
///     <see cref="Resolved" /> is null when the target does not exist in the index.
///     <see cref="ExpectedTypeName" /> is populated when the schema declares a target type.
/// </summary>
public sealed record XmlReferenceFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string TargetId,
    GameSymbol? Resolved,
    string? ExpectedTypeName) : XmlFact(DocumentUri, Line, Column, Length);