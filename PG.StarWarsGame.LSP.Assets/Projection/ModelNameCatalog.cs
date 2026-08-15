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
///         loader (the vendored <see cref="PG.StarWarsGame.Files.ALO.Services.IAloFileService" />), which
///         exposes no mesh names; those come from
///         <see cref="Models.AloModelReader" />, read in
///         <see cref="Models.AloReadOptions.SkipGeometry" /> mode so a whole-repository scan does not
///         decode millions of vertices it will never look at.
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

        foreach (var mesh in ReadMeshNames(aloBytes))
            if (seen.Add(mesh))
                result.Add(mesh);

        return result;
    }

    /// <summary>
    ///     The model's mesh names, or none when the file will not parse.
    /// </summary>
    /// <remarks>
    ///     <see cref="Models.AloModelReader" /> is deliberately strict - a preview must refuse a
    ///     malformed model rather than draw a plausible-looking wrong one. This catalog has the
    ///     opposite contract: it scans every model in a repository and one bad file must not cost the
    ///     caller every other model's names. Reconciling the two is this catch, and it belongs here
    ///     rather than in the reader, because leniency is this scan's requirement and nobody else's.
    ///     The bones already collected are still returned, so a model that fails here degrades to
    ///     skeleton-only rather than to nothing.
    /// </remarks>
    private static IEnumerable<string> ReadMeshNames(byte[] aloBytes)
    {
        try
        {
            return Models.AloModelReader
                .Read(aloBytes, Models.AloReadOptions.SkipGeometry)
                .Meshes.Select(m => m.Name)
                .Where(n => n.Length > 0)
                .ToList();
        }
        catch (Models.AloFormatException)
        {
            return [];
        }
    }
}
