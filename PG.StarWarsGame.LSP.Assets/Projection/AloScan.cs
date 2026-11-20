// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>What one <c>.alo</c> is, and the textures and particle systems it carries inside itself.</summary>
/// <param name="Kind">Model or particle system, from the root chunk; unknown for anything else.</param>
/// <param name="Textures">The textures it names; empty when it names none or cannot be read.</param>
public sealed record AloScan(AloFileKind Kind, IReadOnlyList<string> Textures)
{
    public static readonly AloScan Unreadable = new(AloFileKind.Unknown, []);

    /// <summary>
    ///     A model's proxy names, as written - with the level tags the engine strips before loading
    ///     the particle system each names (#270). Empty for a particle system or an unreadable file.
    /// </summary>
    public IReadOnlyList<string> Proxies { get; init; } = [];

    /// <summary>
    ///     A model's meshes and proxies, in file order, each with whether the file marks it visible
    ///     and whether a mesh is drawn as a surface (#449). Null for a particle system or a file that
    ///     could not be read: unknown, which a rule must keep apart from "no parts".
    /// </summary>
    public IReadOnlyList<ModelPart>? Parts { get; init; }
}