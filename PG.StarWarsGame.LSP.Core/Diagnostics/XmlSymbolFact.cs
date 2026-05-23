// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a symbol with the given ID has more than one definition in the workspace.
///     All definitions (including this one) are included in <see cref="AllDefinitions" />.
/// </summary>
public sealed record XmlSymbolFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string SymbolId,
    IReadOnlyList<GameSymbol> AllDefinitions) : XmlFact(DocumentUri, Line, Column, Length);