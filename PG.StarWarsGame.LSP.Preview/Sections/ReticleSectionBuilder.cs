// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Preview.Sections;

/// <summary>
///     The reticles a scene's targetable hardpoints are drawn with: one state set per hardpoint type
///     the tree's reticle table knows, and a notice for each type it does not (#314).
/// </summary>
public sealed class ReticleSectionBuilder(IVariantTagSource tagSource, IMessageCatalog messages)
{
    /// <summary>
    ///     The reticle art for the hardpoint types this subject actually mounts, without the images.
    /// </summary>
    /// <remarks>
    ///     Only the types present, not all thirteen. The PNGs are attached later by
    ///     <c>GetPreviewSceneHandler</c>, which is where the icon catalog lives - resolving them is
    ///     asynchronous and this builder is not.
    /// </remarks>
    public PreviewReticles? Reticles(GameIndex index,
        IReadOnlyList<PreviewHardpoint> hardpoints, List<PreviewProblem> problems)
    {
        var targetable = hardpoints
            .Where(h => h.IsTargetable && !string.IsNullOrEmpty(h.Type))
            .Select(h => h.Type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetable.Count == 0)
            return null;

        var map = PreviewReticleMap.From(index, tagSource);
        var byType = new Dictionary<string, PreviewReticleStates>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in targetable)
            if (map.ForType(type) is { } states)
                byType[type] = states;
            else
                problems.Add(new PreviewProblem(DiagnosticIds.PreviewNoReticleForType, "info",
                    messages.Format("PreviewScene_NoReticle", type)));

        return new PreviewReticles(byType, new Dictionary<string, string>(),
            map.EnemyScreenSize, map.FriendlyScreenSize);
    }
}