// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Assets;

/// <summary>
///     The textures a model or particle file names inside itself.
/// </summary>
/// <remarks>
///     <para>
///         Declared in Core, and implemented where an .alo can actually be parsed. The Xml project
///         references Core and nothing else, so a diagnostics handler cannot open a binary asset -
///         which is precisely why the textures a model names were never validated at all, and the
///         only thing that ever asked whether they resolved was the 3D preview at draw time.
///     </para>
///     <para>
///         Same shape as <see cref="Diagnostics.IIconRepackStatusProvider" />: the host supplies it,
///         and a host that does not simply gets no diagnostics of this kind.
///     </para>
/// </remarks>
public interface IModelTextureIndex
{
    /// <summary>
    ///     Every texture name the model references, in file order and with duplicates left in.
    /// </summary>
    /// <param name="modelReference">
    ///     The reference as the XML wrote it - a bare filename as often as a path. Resolving it is
    ///     the implementation's business, because it is the half that owns the asset catalog.
    /// </param>
    /// <returns>
    ///     Empty when the model does not resolve, cannot be parsed, or names no textures. All three
    ///     mean the same thing to a caller: there is nothing here to report on.
    /// </returns>
    IReadOnlyList<string> TexturesOf(string modelReference);
}
