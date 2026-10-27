// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: an entry in the tail of a <c>Damage_Types</c> list is not the one the engine
///     reads at that position. The span is the entry as written.
/// </summary>
/// <param name="Token">The entry as written.</param>
/// <param name="Expected">The entry the engine reads at this position.</param>
/// <param name="Required">How many entries the engine's tail has.</param>
public sealed record DamageTypeOutOfOrderFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string Token,
    string Expected,
    int Required) : XmlFact(DocumentUri, Line, Column, Length);