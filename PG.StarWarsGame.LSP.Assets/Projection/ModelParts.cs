// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     A model's meshes and proxies, reduced to the facts the catalogue keeps of them (#449).
/// </summary>
/// <remarks>
///     <para>
///         Which meshes are drawn as a surface follows the preview's own list of materials the game
///         uses but never shows (<c>materials.ts</c>, <c>NON_VISUAL</c>): the shadow volume, the
///         collision hull, the light visualiser, the occluded-unit pass and the heat pass, matched on
///         the material name the model file holds because each has a <c>Mesh</c> and an <c>RSkin</c>
///         form. One list on both sides, so the preview and the catalogue cannot disagree on what a
///         mesh is. Only the resulting boolean leaves this class.
///     </para>
///     <para>
///         A mesh is a surface when any of its sub-meshes is: one shown pass is enough to draw it.
///     </para>
/// </remarks>
public static class ModelParts
{
    private static readonly string[] NeverShownMaterials =
        ["shadowvolume", "collision", "lightvisualize", "occludedunit", "heat"];

    public static IReadOnlyList<ModelPart> Of(AlamoModelContent model)
    {
        return
        [
            .. model.Meshes.Select(m => new ModelPart(m.Name, false, m.IsVisible, IsSurface(m))),
            .. model.Proxies.Select(p => new ModelPart(p.Name, true, p.IsVisible, false))
        ];
    }

    /// <summary>Whether the mesh is drawn as a surface at all.</summary>
    public static bool IsSurface(AlamoMesh mesh)
    {
        return mesh.SubMeshes.Any(sub => !NeverShownMaterials.Any(token =>
            sub.Shader.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }
}