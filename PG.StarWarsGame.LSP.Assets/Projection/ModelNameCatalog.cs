// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Produces the set of names an XML bone reference can legitimately resolve to on a model:
///     the model's skeleton bones <em>unioned with</em> its mesh names.
/// </summary>
/// <remarks>
///     <para>
///         The Alamo engine synthesises a bone at every mesh's origin at runtime, so
///         <c>Attachment_Bone</c>, <c>Collision_Mesh</c>, <c>Damage_Decal</c>, <c>Damage_Particles</c>,
///         <c>Engine_Particles</c> and the fire/turret bones all match either a real skeleton bone or a
///         mesh name. Validating against bones alone yields false positives for meshes that were never
///         given a dedicated bone (e.g. the <c>*_BLAST</c> damage decals on the higher-tier stations).
///     </para>
///     <para>
///         This type is the single, stable seam for that union. Bones come from the caller-supplied
///         loader (the vendored <see cref="PG.StarWarsGame.Files.ALO.Services.IAloFileService" />); mesh
///         names are recovered by the deprecated <see cref="AloMeshNameReader" /> shim. When the ALO
///         loader gains a native <c>AlamoModel.Meshes</c>, swap the shim call below for it and delete the
///         shim - no consumer of this method needs to change.
///     </para>
/// </remarks>
public static class ModelNameCatalog
{
    /// <summary>
    ///     Returns <paramref name="boneExtractor" />'s bones (verbatim, duplicates preserved) followed
    ///     by every mesh name in <paramref name="aloBytes" /> not already present as a bone. Mesh-name
    ///     additions are de-duplicated case-insensitively, matching how the bone catalog is keyed.
    /// </summary>
    /// <param name="aloBytes">Raw bytes of the <c>.alo</c> model.</param>
    /// <param name="boneExtractor">
    ///     Reads the skeleton bones from the same bytes (production: the ALO loader). Kept as an
    ///     injected delegate so this seam stays decoupled from the engine's DI graph and testable
    ///     without a binary skeleton fixture.
    /// </param>
    public static IReadOnlyList<string> ReadBoneReferenceTargets(
        byte[] aloBytes, Func<byte[], IReadOnlyList<string>> boneExtractor)
    {
        var bones = boneExtractor(aloBytes) ?? [];
        var result = new List<string>(bones);

        // Seeded from the bones so a mesh sharing a bone's name (the L1 blast decals are both) is not
        // added twice; OrdinalIgnoreCase mirrors ModelBoneKey / the hardpoint bone lookup.
        var seen = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);

#pragma warning disable CS0618 // deliberate: the shim is the only mesh-name source until the loader exposes Meshes
        foreach (var mesh in AloMeshNameReader.ReadMeshNames(aloBytes))
#pragma warning restore CS0618
            if (seen.Add(mesh))
                result.Add(mesh);

        return result;
    }
}
