// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a game object's name has the same hash as the names of <see cref="Others" /> -
///     see <see cref="IObjectNameHash" />. Positioned on the object's definition.
/// </summary>
public sealed record XmlNameCrcCollisionFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string SymbolId,
    uint Hash,
    IReadOnlyList<GameSymbol> Others) : XmlFact(DocumentUri, Line, Column, Length);
