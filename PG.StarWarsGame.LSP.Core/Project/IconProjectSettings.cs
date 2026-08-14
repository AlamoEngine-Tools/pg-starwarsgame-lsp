// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Project;

/// <summary>
///     Where a project keeps its UI icons: the mega texture pair the game reads, and any folders of
///     raw source images a packer builds that pair from.
/// </summary>
/// <param name="MegaTexture">
///     Path to the mega texture pair, relative to the .pgproj and WITHOUT an extension - it names
///     two files, <c>.mtd</c> (the directory of rectangles) and <c>.tga</c> (the atlas itself).
/// </param>
/// <param name="SourceRoots">
///     Folders holding raw icon sources, in priority order. Optional: a mod that ships only a packed
///     mega texture needs none.
/// </param>
/// <remarks>
///     Unlike <see cref="LocalisationProjectSettings" />, this has a meaningful default - the engine
///     always looks for the same mega texture in the same place - so a project that declares no
///     <c>icons</c> node still gets working icons via <see cref="Default" />. Callers should reach
///     for <c>project.Icons ?? IconProjectSettings.Default</c> rather than treating absence as
///     "no icon support".
/// </remarks>
public sealed record IconProjectSettings(string MegaTexture, IReadOnlyList<string> SourceRoots)
{
    /// <summary>The mega texture every unmodified game ships, relative to the game root.</summary>
    public const string ConventionalMegaTexture = "data/art/textures/mt_commandbar";

    /// <summary>Settings for a project that declares no <c>icons</c> node.</summary>
    public static IconProjectSettings Default { get; } = new(ConventionalMegaTexture, []);

    /// <summary>Path to the mega texture directory file (<c>.mtd</c>).</summary>
    public string MtdPath => MegaTexture + ".mtd";

    /// <summary>Path to the mega texture itself (<c>.tga</c>).</summary>
    public string TexturePath => MegaTexture + ".tga";
}
