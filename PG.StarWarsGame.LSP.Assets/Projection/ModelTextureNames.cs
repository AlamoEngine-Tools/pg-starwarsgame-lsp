// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     The texture names an <c>.alo</c> carries inside itself - a model's skins, a particle
///     system's sprite sheets.
/// </summary>
/// <remarks>
///     <para>
///         Here rather than in the server so both catalogs can be built from the same bytes the
///         bone pass already reads: the BaselineBuilder for the shipped game, and the workspace
///         indexer for a mod's own models.
///     </para>
///     <para>
///         Why it matters: these were parsed LAZILY, during validation, the first time any
///         document named the model. MEASURED 2026-09-30 on a real layered mod - one prop file
///         spent 17.2s of its 17.5s here, and six such files were 52s of an 83s workspace sweep.
///         Doing it in the pass that already opens every model makes it free at validation time.
///     </para>
/// </remarks>
public static class ModelTextureNames
{
    /// <summary>
    ///     Every texture the file names, in declaration order and with duplicates kept (callers
    ///     dedupe by what they report on). Empty for anything that is not a readable model or
    ///     particle system - a file we cannot parse is not a missing texture, and claiming one
    ///     would be a lie.
    /// </summary>
    public static IReadOnlyList<string> Read(byte[] bytes)
    {
        if (bytes.Length == 0) return [];

        try
        {
            // A particle system and a model are both .alo and are told apart by their first chunk,
            // exactly as the preview does it. Reading one as the other throws rather than lying.
            return AloFile.Classify(bytes) switch
            {
                AloFileKind.Particle => ParticleTextures(bytes),
                AloFileKind.Model => ModelTextures(bytes),
                _ => []
            };
        }
        catch
        {
            // Corrupt, unsupported, or not a model at all. ModelFileFormat is the diagnostic that
            // owns "this .alo is not readable"; one bad file must never abort the scan.
            return [];
        }
    }

    private static IReadOnlyList<string> ModelTextures(byte[] bytes)
    {
        var names = new List<string>();

        foreach (var mesh in AloModelReader.Read(bytes).Meshes)
        foreach (var sub in mesh.SubMeshes)
        foreach (var parameter in sub.Parameters)
            if (parameter.Type == AlamoShaderParameterType.Texture
                && !string.IsNullOrWhiteSpace(parameter.Texture))
                names.Add(parameter.Texture!);

        return names;
    }

    private static IReadOnlyList<string> ParticleTextures(byte[] bytes)
    {
        var names = new List<string>();

        foreach (var emitter in AloParticleReader.Read(bytes).Emitters)
        {
            if (!string.IsNullOrWhiteSpace(emitter.ColorTexture))
                names.Add(emitter.ColorTexture);

            if (!string.IsNullOrWhiteSpace(emitter.NormalTexture))
                names.Add(emitter.NormalTexture);
        }

        return names;
    }
}
