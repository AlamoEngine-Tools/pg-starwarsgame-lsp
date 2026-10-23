// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>
///     An enum's values as the game takes them (#270): a built-in enum's are the schema's; a dynamic
///     one's are the game's and the project's together, from their enum files, as validation merges
///     them - the game's first, then each the project adds. The schema describes the ones it knows.
/// </summary>
internal static class EnumValueList
{
    /// <param name="Name">The key: the name XML writes.</param>
    /// <param name="EngineValue">What the engine uses for it; the name where the schema gives no other.</param>
    /// <param name="Origin"><c>game</c>, or <c>project</c> for a value only the project's files add.</param>
    public sealed record Value(
        string Name,
        string EngineValue,
        string Origin,
        IReadOnlyDictionary<string, string>? Description);

    public static IReadOnlyList<Value> Of(GameIndex index, EnumDefinition definition)
    {
        var declared = definition.Values
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var shipped = index.Baseline.DynamicEnumValues.GetValueOrDefault(definition.Name, []);
        var added = index.WorkspaceDynamicEnumValues.GetValueOrDefault(definition.Name, []);
        // A dynamic enum the index has no values for - not read yet, or one the files leave empty -
        // falls back to what the schema says, as a built-in one always does.
        if (definition.Kind == EnumKind.SchemaFixed || (shipped.IsEmpty && added.IsEmpty))
            return [.. definition.Values.Select(v => new Value(v.Name, v.EngineValue, "game", v.Description))];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<Value> values = [];
        foreach (var name in shipped)
            if (seen.Add(name))
                values.Add(Of(name, "game", declared.GetValueOrDefault(name)));
        foreach (var name in added)
            if (seen.Add(name))
                values.Add(Of(name, "project", declared.GetValueOrDefault(name)));
        return values;
    }

    private static Value Of(string name, string origin, EnumValueDefinition? declared)
    {
        return new Value(name, declared?.Value ?? name, origin, declared?.Description);
    }

    /// <summary>Whether the enum is built into the engine or read from the game's files, as a key the panel names.</summary>
    public static string KindKey(EnumKind kind)
    {
        return kind switch
        {
            EnumKind.SchemaFixed => "fixed",
            EnumKind.DynamicXml => "dynamic",
            _ => "gameConstants"
        };
    }
}