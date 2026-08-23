// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Turns the reticle names <c>GameConstants</c> gives into the images the client draws.
/// </summary>
/// <remarks>
///     <para>
///         Through the GAME ASSET resolver, not the icon catalog. That was the mistake chunk R1
///         shipped and a live server exposed: the icon catalog scans a project's own
///         <c>SourceRoots</c>, which are empty unless a <c>pgproj</c> declares them, and it exists
///         for art a modder is drawing rather than for the game's own textures. A reticle is a
///         texture like any other, so it resolves out of <c>Data/Art/Textures/</c> through the tier
///         chain - the workspace shadowing the expansion shadowing the base game - exactly as
///         <see cref="GetModelTextureHandler" /> has always done.
///     </para>
///     <para>
///         Decoded server-side to PNG rather than shipped raw. The client already decodes textures
///         for materials, but a reticle is chrome: it is wanted the moment the scene arrives, it is
///         64x64, and five of them cover a whole capital ship. Inlining them costs one small string
///         and saves a round trip per family before anything can be drawn.
///     </para>
/// </remarks>
public static class PreviewReticleIcons
{
    /// <summary>Where the engine keeps textures a name refers to without any path.</summary>
    private const string TextureDirectory = "Data/Art/Textures/";

    /// <summary>
    ///     The extensions tried, in order.
    /// </summary>
    /// <remarks>
    ///     <c>GameConstants</c> writes the name with no suffix at all. Every shipped reticle is a
    ///     <c>.dds</c>, so that is first; a mod is free to replace one with a <c>.tga</c> the same way
    ///     it may for any other texture the engine loads by bare name.
    /// </remarks>
    private static readonly string[] Extensions = [".dds", ".tga", ".png"];

    /// <summary>
    ///     Resolves each distinct name to a <c>data:image/png;base64,...</c> URI.
    /// </summary>
    /// <remarks>
    ///     A name that resolves nowhere, or resolves to something that will not decode, is left OUT
    ///     of the result rather than raising anything. A scene reads perfectly well without
    ///     reticles - a workspace with no game directory configured has no textures at all - and the
    ///     DTO documents the absence as a quiet omission.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Resolve(
        IGameAssetResolver assets, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(names);

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || resolved.ContainsKey(name))
                continue;

            // Case-insensitively keyed, so the same family named twice with different casing - which
            // the XML does - is read once.
            var png = Decode(assets, name.Trim());
            if (png is not null)
                resolved[name.Trim()] = "data:image/png;base64," + Convert.ToBase64String(png);
        }

        return resolved;
    }

    private static byte[]? Decode(IGameAssetResolver assets, string name)
    {
        foreach (var extension in Extensions)
        {
            var bytes = assets.Read(TextureDirectory + name + extension);
            if (bytes is null)
                continue;

            // A PNG is already what the client wants; anything else goes through the surface reader
            // that the loose icon sources use, which is the same Pfim/TGA path and the same writer.
            try
            {
                if (extension == ".png")
                    return bytes;

                using var stream = new MemoryStream(bytes);
                var surface = ImageSurface.Load(stream);

                return PngWriter.Write(surface.Width, surface.Height,
                    surface.Crop(0, 0, surface.Width, surface.Height));
            }
            catch
            {
                // A file that will not decode is the same to the reader as one that is not there:
                // no reticle. Trying the next extension costs nothing and may find a good one.
            }
        }

        return null;
    }
}
