// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>How the engine finds one kind of asset file, and how badly it takes one going missing.</summary>
/// <param name="NounKey">The catalogue key of the asset as a reader names it: Model, Texture, Audio, Map.</param>
/// <param name="AllowedExtensions">The file types that can satisfy a name of this kind.</param>
/// <param name="TextureLookup">
///     Whether a name of this kind is looked up as the game looks up a texture: as written, then its
///     first-dot stem as a <c>.dds</c> (<see cref="GameTextureName.Candidates" />). Otherwise only the name
///     as written satisfies it.
/// </param>
/// <param name="ResolvesFromMegaTexture">Whether art packed into a mega texture satisfies the name.</param>
/// <param name="MissingSeverity">What a missing file costs the game.</param>
public sealed record AssetKindRule(
    string NounKey,
    IReadOnlyList<string> AllowedExtensions,
    bool TextureLookup,
    bool ResolvesFromMegaTexture,
    XmlDiagnosticSeverity MissingSeverity);

/// <summary>
///     The asset rules per <see cref="ReferenceKind" />, in one place, so a whole asset tag and an
///     asset item of a tuple cannot disagree about what satisfies a name.
/// </summary>
public static class AssetKindRules
{
    private static readonly Dictionary<ReferenceKind, AssetKindRule> Rules = new()
    {
        // A missing model is drawn as nothing; the game runs.
        [ReferenceKind.ModelFile] =
            new AssetKindRule("AssetKind_Model", [".alo"], false, false, XmlDiagnosticSeverity.Warning),

        // Measured in the game (FoC x64, 2024 release, #420): the name as written, then its first-dot stem
        // as a .dds, nothing else; the file is decoded by its content, so a PNG is a texture like the rest.
        // GUI art - unit and ability icons, cursors, encyclopedia chrome - ships INSIDE a mega
        // texture rather than as a file, so the file lookup alone declared perfectly good art missing.
        [ReferenceKind.TextureFile] =
            new AssetKindRule("AssetKind_Texture", GameTextureName.Extensions, true, true,
                XmlDiagnosticSeverity.Warning),

        [ReferenceKind.AudioFile] =
            new AssetKindRule("AssetKind_Audio", [".wav", ".mp3"], false, false, XmlDiagnosticSeverity.Warning),

        // An Error, not a Warning (issue #132). Measured in the 2018 build: the engine opens the
        // name as given, and on failure retries it against the resolved map path with the extension
        // forced to .ted - so it is forgiving about how the map is spelled. When that second open
        // also fails it asserts and returns false. Nothing stands in for it: the hardcoded
        // _Desert_L5_01.ted and _Space_Temperate1.ted defaults belong to the EMPTY-name path, which
        // a misspelled name never reaches. So unlike a missing model or texture, this does not
        // degrade the battle - there is no battle.
        [ReferenceKind.MapFile] =
            new AssetKindRule("AssetKind_Map", [".ted"], false, false, XmlDiagnosticSeverity.Error)
    };

    /// <summary>The rule for an asset kind, or null for a kind that names no asset file.</summary>
    public static AssetKindRule? For(ReferenceKind kind)
    {
        return Rules.GetValueOrDefault(kind);
    }
}