// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Shared cross-language reference validation. Used by both the XML diagnostics pipeline and
///     the Lua diagnostics publisher so that the GameObjectType wildcard exemption, message text,
///     and severity are consistent across languages.
/// </summary>
public static class ReferenceResolutionEvaluator
{
    /// <summary>
    ///     Evaluates a reference and returns a diagnostic, or <c>null</c> when the reference is valid.
    /// </summary>
    public static (XmlDiagnosticSeverity Severity, string Message)? Evaluate(
        string targetId, string? expectedTypeName, GameSymbol? resolved)
    {
        // Scoped ability IDs are stored as "OWNER$name"; show only the bare name to the user.
        var displayId = StripOwnerPrefix(targetId);

        if (resolved is null)
            return (XmlDiagnosticSeverity.Error,
                $"Cannot resolve reference '{displayId}': no object with this name exists in the workspace.");

        if (expectedTypeName is null)
            return null;

        if (string.Equals(expectedTypeName, "GameObjectType", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(resolved.TypeName, expectedTypeName, StringComparison.OrdinalIgnoreCase))
            return null;

        // SpecialAbility has no schema-level type hierarchy (types.yaml lists concrete ability
        // subtypes as flat siblings) - consult the hardcoded family allowlist instead of requiring
        // an exact match, so e.g. GUI_Activated_Ability_Name accepts any concrete ability type.
        if (string.Equals(expectedTypeName, "SpecialAbility", StringComparison.OrdinalIgnoreCase) &&
            resolved.TypeName is not null && SpecialAbilityTypeFamily.TypeNames.Contains(resolved.TypeName))
            return null;

        return (XmlDiagnosticSeverity.Error,
            $"Type mismatch for '{displayId}': expected '{expectedTypeName}' but found '{resolved.TypeName}'.");
    }

    /// <summary>
    ///     Returns the user-visible portion of a symbol ID, stripping an owner prefix separated by
    ///     <c>$</c> (e.g. <c>"MY_UNIT$Medic_Healing"</c> → <c>"Medic_Healing"</c>).
    ///     IDs without a <c>$</c> are returned as-is.
    /// </summary>
    public static string StripOwnerPrefix(string id)
    {
        var idx = id.IndexOf('$');
        return idx >= 0 ? id[(idx + 1)..] : id;
    }
}