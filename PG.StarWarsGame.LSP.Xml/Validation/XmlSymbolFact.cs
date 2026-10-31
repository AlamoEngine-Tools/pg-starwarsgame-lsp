// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     Observation: a symbol with the given ID has more than one definition in the workspace.
///     All definitions (including this one) are included in <see cref="AllDefinitions" />.
///     When the load order places every definition in one registry, <see cref="Kept" /> is the one
///     the registry's <see cref="Rule" /> keeps and this one is dropped or merged into it;
///     otherwise all three are null and nothing says which the game uses.
/// </summary>
public sealed record XmlSymbolFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string SymbolId,
    IReadOnlyList<GameSymbol> AllDefinitions,
    GameSymbol? Kept = null,
    DuplicateNameRule? Rule = null,
    string? Registry = null) : XmlFact(DocumentUri, Line, Column, Length);