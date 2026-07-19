// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Data;

namespace PG.StarWarsGame.LSP.Server.Localisation;

// Shared "what would this key look like if this project didn't override it" merge, used by both
// the "Inherited" baseline overlay and DAT export so a .pgproj dependency's own translations are
// visible, not just the shipped game baseline.
public static class LocalisationLayerMerge
{
    // Merges every database in baselineDatabases (lowest precedence) into target, then every
    // registered layer ranked strictly below belowRank (nearest-first-wins - the layer closest to
    // belowRank is merged last so it overrides farther dependencies and the baseline). Pass null
    // for belowRank to merge baseline only (no file/layer context available).
    public static void MergeBaselineAndLowerLayers(
        IKeyedTranslationDatabase target,
        IEnumerable<IKeyedTranslationDatabase> baselineDatabases,
        IReadOnlyList<LocalisationLayerEntry> allLayers,
        int? belowRank)
    {
        foreach (var baseline in baselineDatabases)
            MergeInto(target, baseline);

        if (belowRank is not { } rank) return;

        foreach (var entry in allLayers.Where(e => e.Layer.Rank < rank).OrderBy(e => e.Layer.Rank))
            MergeInto(target, entry.Database);
    }

    private static void MergeInto(IKeyedTranslationDatabase target, IEnumerable<TranslationEntry> source)
    {
        foreach (var entry in source)
        foreach (var kv in entry.Translations)
            target.SetTranslation(entry.Key, kv.Key, kv.Value);
    }
}