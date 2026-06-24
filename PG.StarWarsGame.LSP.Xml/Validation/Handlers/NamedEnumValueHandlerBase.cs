// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Base for single-value handlers whose tag carries an <see cref="EnumDefinition" />.
///     Subclasses get <see cref="GetValidValues" /> for free and inherit a default
///     <see cref="HandleValue" /> that does: non-empty check → enum lookup → Error.
///     Override <see cref="HandleValue" /> when extra structural logic is needed (e.g. flag-lists).
/// </summary>
public abstract class NamedEnumValueHandlerBase : SingleValueTypeHandlerBase
{
    /// <summary>
    ///     Returns the valid value set for <paramref name="enumDef" />, or <c>null</c> when
    ///     validation should be skipped (no enum definition, empty baseline, or open-world kind).
    ///     <list type="bullet">
    ///         <item><see cref="EnumKind.SchemaFixed" /> — returns schema-defined values (always non-null when values exist).</item>
    ///         <item><see cref="EnumKind.DynamicXml" /> — returns baseline values only when the baseline is non-empty.</item>
    ///         <item>Other / null — returns <c>null</c> (skip).</item>
    ///     </list>
    /// </summary>
    protected static HashSet<string>? GetValidValues(EnumDefinition? enumDef, DiagnosticsContext ctx)
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

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (trimmed.Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'' is not a valid value for <{fact.Tag.Tag}>.")
            ];

        var valid = GetValidValues(fact.Tag.Enum, ctx);
        if (valid is not null && !valid.Contains(trimmed))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a known value for <{fact.Tag.Tag}>.")
            ];

        return [];
    }
}
