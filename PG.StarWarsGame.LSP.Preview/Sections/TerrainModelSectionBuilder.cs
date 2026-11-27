// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Preview.Sections;

/// <summary>
///     A land unit's model per map environment, from <c>Land_Terrain_Model_Mapping</c>.
/// </summary>
/// <remarks>
///     The rule as the schema records it: each pair is <c>Environment, Model</c>, the environment
///     matched ignoring case and surrounding spaces; the first pair for the map's environment
///     replaces <c>Land_Model_Name</c>, and any other environment keeps it. Repeats append and a
///     variant's pairs replace the base's - both are the effective object's doing, so this reads
///     every occurrence it carries, in order.
/// </remarks>
internal static class TerrainModelSectionBuilder
{
    public const string MappingTag = "Land_Terrain_Model_Mapping";

    // The shape to read the value with when the schema does not know the tag: plain pairs.
    private static readonly XmlTagDefinition Pairs = new() { Tag = MappingTag, ValueType = XmlValueType.TupleList };

    /// <summary>Each environment the unit maps, once, with the model of its first pair, in written order.</summary>
    public static IReadOnlyList<PreviewTerrainModel> TerrainModels(EffectiveObject effective, ISchemaProvider schema)
    {
        if (effective.Tags.IsDefaultOrEmpty)
            return [];

        var tag = schema.GetTag(MappingTag) ?? Pairs;
        var models = new List<PreviewTerrainModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var occurrence in effective.Tags.Where(t =>
                     t.TagName.Equals(MappingTag, StringComparison.OrdinalIgnoreCase)))
        {
            var items = TupleItems.Read(tag, occurrence.Value);

            // A trailing environment with no model is half a pair, and names nothing.
            for (var i = 0; i + 1 < items.Count; i += 2)
                if (seen.Add(items[i].Text))
                    models.Add(new PreviewTerrainModel(items[i].Text, items[i + 1].Text));
        }

        return models;
    }

    /// <summary>The entry for <paramref name="environment" />, or null when the unit does not map it.</summary>
    public static PreviewTerrainModel? For(IReadOnlyList<PreviewTerrainModel> models, string? environment)
    {
        var wanted = environment?.Trim();
        return string.IsNullOrEmpty(wanted)
            ? null
            : models.FirstOrDefault(m => m.Environment.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }
}