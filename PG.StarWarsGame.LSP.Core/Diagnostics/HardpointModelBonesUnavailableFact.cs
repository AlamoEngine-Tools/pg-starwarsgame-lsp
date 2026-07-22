// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     A model involved in hardpoint bone validation has no readable bone list, so the bones pointing
///     at it could not be checked (#53). Surfaced rather than swallowed: silence would be
///     indistinguishable from "all bones are fine", and the usual causes - a missing, corrupt, or
///     unsupported .alo - are worth knowing about on their own.
/// </summary>
/// <param name="ModelName">The model whose bones could not be read.</param>
/// <param name="OwnerId">The object declaring that model.</param>
/// <param name="UncheckedBoneCount">How many bone references went unvalidated as a result.</param>
public sealed record HardpointModelBonesUnavailableFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ModelName,
    string OwnerId,
    int UncheckedBoneCount
) : XmlFact(DocumentUri, Line, Column, Length);
