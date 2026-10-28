// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Localisation;

/// <summary>
///     Refuses a localisation write to a file of a dependency layer (#276).
///     <para>
///         A dependency's text files are listed beside the project's own, so the editor can show
///         what the layers below already say - but they belong to another project, and a leaf
///         that rewrote them would change that project for every other mod built on it. The story
///         editor already refused its dependency documents; the localisation endpoints wrote
///         straight through.
///     </para>
/// </summary>
public static class LocalisationLayerGuard
{
    /// <summary>
    ///     Why <paramref name="filePath" /> may not be written, or <see langword="null" /> when it
    ///     belongs to the project itself - or to no layer at all, as before any project is loaded.
    /// </summary>
    public static string? ReadOnlyObjection(
        IProjectLayerMap layers, IFileHelper fileHelper, string filePath, IMessageCatalog messages)
    {
        if (layers.Layers.Count == 0)
            return null;

        var leaf = layers.Layers.Max(l => l.Rank);
        var rank = layers.GetRank(fileHelper.NormalizeUri(filePath));
        if (rank >= leaf)
            return null;

        var layer = layers.GetLayerName(rank) ?? messages.Format("LocLayer_ADependency");
        return messages.Format("LocLayer_ReadOnly", filePath, layer);
    }
}