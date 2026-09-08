// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Assets;

/// <summary>
///     Reads the texture names out of an .alo, so the XML diagnostics can check them.
/// </summary>
/// <remarks>
///     <para>
///         The half of <see cref="IModelTextureIndex" /> that needs the asset layer. The Xml project
///         references Core alone and cannot open a binary, which is why the textures a model names
///         inside itself went unvalidated until now - the 3D preview was the only thing that ever
///         asked, at draw time, in the webview.
///     </para>
///     <para>
///         <strong>Cached, because this runs inside validation.</strong> A single unit file names
///         hundreds of models, and re-reading and re-parsing an .alo for each tag on every keystroke
///         would be felt. What is cached is only the list of NAMES a model carries; whether each one
///         resolves is looked up fresh against the asset catalog every time, so dropping a missing
///         texture into the workspace clears the warning without touching this cache.
///     </para>
/// </remarks>
public sealed class ModelTextureIndex(
    IGameAssetResolver assets, ILogger<ModelTextureIndex> logger) : IModelTextureIndex
{
    /// <summary>
    ///     Parsed texture lists, by the reference as the XML wrote it.
    /// </summary>
    /// <remarks>
    ///     Keyed on the raw reference rather than the resolved path so the common case - the same
    ///     name written in twenty files - is one parse, and case-insensitively because the shipped
    ///     data and mod data disagree about casing constantly.
    /// </remarks>
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<string> TexturesOf(string modelReference)
    {
        if (string.IsNullOrWhiteSpace(modelReference))
            return [];

        return _cache.GetOrAdd(modelReference.Trim(), Read);
    }

    /// <summary>Forgets everything, for when the asset layers underneath have moved.</summary>
    public void Clear()
    {
        _cache.Clear();
    }

    private IReadOnlyList<string> Read(string modelReference)
    {
        var bytes = assets.Read(PreviewModelReference.ModelPath(modelReference));

        if (bytes is null || bytes.Length == 0)
            return [];

        try
        {
            // A particle system and a model are both .alo and are told apart by their first chunk,
            // exactly as the preview does it. Reading one as the other throws rather than lying.
            return AloFile.Classify(bytes) switch
            {
                AloFileKind.Particle => ParticleTextures(bytes),
                AloFileKind.Model => ModelTextures(bytes),
                _ => [],
            };
        }
        catch (Exception e)
        {
            // A file we cannot parse is not a missing texture, and reporting one would be a lie.
            // ModelFileFormat is the diagnostic that owns "this .alo is not readable".
            logger.LogDebug(e, "Could not read the textures of '{Model}'", modelReference);
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
