// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     An object turns <c>Fires_Forward</c> on. The position is the tag's value.
/// </summary>
/// <remarks>
///     Carries only the object id: whether the flag does anything depends on the object's behaviours
///     and turret extents, which are often inherited, so the handler resolves the effective object
///     rather than the rule reading the document node.
/// </remarks>
public sealed record FiresForwardFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId
) : XmlFact(DocumentUri, Line, Column, Length);