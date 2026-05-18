// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record GameReference(
    string TargetId,
    GameSymbolKind? ExpectedKind,
    string? ExpectedTypeName,
    string DocumentUri,
    int Line,
    int Column,
    int Length
);