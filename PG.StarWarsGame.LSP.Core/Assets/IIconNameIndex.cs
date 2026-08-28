// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Assets;

/// <summary>
///     The GUI art a mega texture holds, by name.
/// </summary>
/// <remarks>
///     <para>
///         Exists because a texture packed into a mega texture is not a file. The game's command bar
///         art - unit icons, ability icons, cursors, the encyclopedia chrome - ships as entries
///         inside a <c>.mtd</c>/<c>.tga</c> pair, so asking the asset file index whether
///         <c>i_button_EV_ExecutorStarDestroyer.tga</c> exists could only ever answer no, and the
///         editor warned about art the preview was drawing perfectly well.
///     </para>
///     <para>
///         Declared in Core for the same reason as <see cref="IModelTextureIndex" />: the Xml project
///         references Core and nothing else, so a diagnostics handler cannot open a mega texture
///         itself. The host supplies this, and a host that does not simply gets the file lookup's
///         own answer.
///     </para>
/// </remarks>
public interface IIconNameIndex
{
    /// <summary>
    ///     Whether a mega texture holds this art.
    /// </summary>
    /// <param name="reference">
    ///     The reference as the XML wrote it, extension and all. Implementations match without it and
    ///     without regard to case: a <c>.mtd</c> records its entries uppercase whatever the packer
    ///     was fed, while the XML writes whatever the author typed.
    /// </param>
    bool Contains(string reference);
}
