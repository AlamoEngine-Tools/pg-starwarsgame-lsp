// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Canonical key for <see cref="BaselineIndex.ModelBones" /> / <see cref="GameIndex.ModelBones" />:
///     the bare <c>.alo</c> filename, lowercased.
///     <para>
///         The engine resolves models by name across its virtual file system, and game XML only ever
///         names a model by its bare filename (e.g. <c>UB_PALACE.ALO</c>) - never a path. The bone
///         catalog must therefore be keyed the same way so a lookup from XML can find it. Both the
///         producer side (MEG catalog / loose-file scan) and the consumer side (bone completion,
///         hardpoint bone validation) run their strings through here, so the two can never disagree
///         on the key shape again - a full-path/filename mismatch is what silently disabled the whole
///         feature before.
///     </para>
/// </summary>
public static class ModelBoneKey
{
    /// <summary>
    ///     Reduces any model reference - a raw XML value, a MEG entry path, or a relative file path,
    ///     in any case and with either slash - to the bare lowercased filename used as the catalog key.
    /// </summary>
    public static string From(string modelReference)
    {
        var normalized = modelReference.Replace('\\', '/').Trim();
        var slash = normalized.LastIndexOf('/');
        var fileName = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        return fileName.ToLowerInvariant();
    }
}
