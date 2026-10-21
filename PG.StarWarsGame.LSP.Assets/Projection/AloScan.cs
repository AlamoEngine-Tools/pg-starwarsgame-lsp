// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>What one <c>.alo</c> is, and the texture names it carries inside itself.</summary>
/// <param name="Kind">Model or particle system, from the root chunk; unknown for anything else.</param>
/// <param name="Textures">The textures it names; empty when it names none or cannot be read.</param>
public sealed record AloScan(AloFileKind Kind, IReadOnlyList<string> Textures)
{
    public static readonly AloScan Unreadable = new(AloFileKind.Unknown, []);
}