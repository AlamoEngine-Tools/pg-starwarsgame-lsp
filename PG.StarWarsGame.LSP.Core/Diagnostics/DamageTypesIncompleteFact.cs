// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a <c>Damage_Types</c> list holds fewer entries than the tail the engine reads at
///     fixed positions from its end, so the game cannot find them. The span is the last entry, or
///     the empty element when there is none.
/// </summary>
/// <param name="Count">How many entries the list holds.</param>
/// <param name="Required">How many the engine's tail needs.</param>
public sealed record DamageTypesIncompleteFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    int Count,
    int Required) : XmlFact(DocumentUri, Line, Column, Length);