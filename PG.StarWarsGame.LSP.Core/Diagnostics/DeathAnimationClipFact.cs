// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     An object writes <c>Specific_Death_Anim_Type</c> (#104). The position is that tag's value.
/// </summary>
/// <remarks>
///     Carries only the object id: the model, the index and <c>Remove_Upon_Death</c> are usually inherited -
///     vanilla's death clones are variants that write nothing but the type - so the handler resolves the
///     effective object.
/// </remarks>
public sealed record DeathAnimationClipFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId
) : XmlFact(DocumentUri, Line, Column, Length);
