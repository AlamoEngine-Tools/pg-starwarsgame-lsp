// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     An object writes a turret-extent tag, which on a WEAPON unit without the TURRET behaviour is the
///     hull's firing arc (A5). The position is that tag's value.
/// </summary>
/// <remarks>
///     Carries only the object id: behaviours, <c>Fires_Forward</c> and the extents themselves are often
///     inherited, so the handler resolves the effective object.
/// </remarks>
public sealed record HullFiringArcFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId
) : XmlFact(DocumentUri, Line, Column, Length);