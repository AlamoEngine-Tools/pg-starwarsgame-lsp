// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>A variant object whose <c>Variant_Of_Existing_Type</c> base chain loops back on itself.</summary>
public sealed record VariantCycleFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId,
    string? CycleObjectId
) : XmlFact(DocumentUri, Line, Column, Length);