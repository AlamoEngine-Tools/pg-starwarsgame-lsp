// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Shared enum value-set resolution for validation handlers (single-value enum tags AND tuple
///     slots that hold an enum value, e.g. InaccuracyMap's category).
/// </summary>
internal static class EnumValueSets
{
    /// <summary>
    ///     Returns the valid value set for <paramref name="enumDef" />, or <c>null</c> when
    ///     validation should be skipped (no enum definition, empty baseline+workspace, or
    ///     open-world kind). SchemaFixed enums use the schema values; DynamicXml enums use
    ///     baseline ∪ workspace.
    /// </summary>
    public static HashSet<string>? GetValidValues(EnumDefinition? enumDef, DiagnosticsContext ctx)
    {
        if (enumDef is null)
            return null;

        if (enumDef is { Kind: EnumKind.SchemaFixed, Values.Count: > 0 })
            return enumDef.Values.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enumDef.Kind == EnumKind.DynamicXml)
        {
            var hasBaseline = ctx.Index.Baseline.DynamicEnumValues.TryGetValue(enumDef.Name, out var baselineVals)
                              && baselineVals.Length > 0;
            var hasWorkspace = ctx.Index.WorkspaceDynamicEnumValues.TryGetValue(enumDef.Name, out var workspaceVals)
                               && workspaceVals.Length > 0;

            if (!hasBaseline && !hasWorkspace)
                return null;

            var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (hasBaseline) merged.UnionWith(baselineVals);
            if (hasWorkspace) merged.UnionWith(workspaceVals);
            return merged;
        }

        return null;
    }
}
