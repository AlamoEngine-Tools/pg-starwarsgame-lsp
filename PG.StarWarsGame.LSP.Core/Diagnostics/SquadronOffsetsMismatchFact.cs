// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

public sealed record SquadronOffsetsMismatchFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    int TotalUnits,
    int TotalOffsets,
    IReadOnlyList<(int Line, int Column, int Length)> UnitTagLocations,
    IReadOnlyList<(int Line, int Column, int Length)> OffsetTagLocations
) : XmlFact(DocumentUri, Line, Column, Length);