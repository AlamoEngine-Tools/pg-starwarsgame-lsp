// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Assets;

/// <summary>
///     How the game turns the texture name a model or particle system carries into the file it
///     opens.
/// </summary>
/// <remarks>
///     <para>
///         MEASURED on the 2024 64-bit build's texture loader: the name is tried as written, then the
///         part after its LAST BACKSLASH is tried under each texture directory; when nothing is found
///         the same is repeated with <c>.dds</c> in place of the extension. A forward slash is not a
///         cut point there.
///     </para>
///     <para>
///         Why it matters: the shipped art names its sprite sheets by the art machine's absolute path
///         in 37 particle files per game - three names in all, such as
///         <c>C:\Projects\Starwars\Art\Textures\Game\p_particle_master.tga</c> - and they render in
///         game because of this rule. Looked up as written they were reported missing everywhere
///         (#292). Every place that resolves a texture name goes through here, so the Mod Browser, the
///         model preview, the particle preview and the XML check agree.
///     </para>
/// </remarks>
public static class GameTextureName
{
    /// <summary>The name the game looks up: what follows the last backslash of <paramref name="named" />, trimmed.</summary>
    public static string FileName(string named)
    {
        var name = named.Trim();
        var cut = name.LastIndexOf('\\');
        return cut < 0 ? name : name[(cut + 1)..];
    }
}