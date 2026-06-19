// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

public sealed record XmlCrossTypeShadowFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string SymbolId,
    string OwnTypeName,
    string CollidingTypeName
) : XmlFact(DocumentUri, Line, Column, Length);