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
        if (resolved is null)
            return (XmlDiagnosticSeverity.Error,
                $"Cannot resolve reference '{targetId}': no object with this name exists in the workspace.");

        if (expectedTypeName is null)
            return null;

        if (string.Equals(expectedTypeName, "GameObjectType", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(resolved.TypeName, expectedTypeName, StringComparison.OrdinalIgnoreCase))
            return null;

        return (XmlDiagnosticSeverity.Error,
            $"Type mismatch for '{targetId}': expected '{expectedTypeName}' but found '{resolved.TypeName}'.");
    }
}
