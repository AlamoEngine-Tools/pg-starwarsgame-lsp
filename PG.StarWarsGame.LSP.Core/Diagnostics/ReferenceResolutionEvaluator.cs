// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
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
    /// <param name="indexedTypeNames">
    ///     Every type name the workspace index holds at least one instance of, or <c>null</c> from a
    ///     caller that does not track it. When an unresolved reference expects a type absent from this
    ///     set, the type is not indexed AT ALL - the name cannot be judged, and saying no such object
    ///     exists would be false. See <see cref="DiagnosticIds.ReferenceTypeNotIndexed" />.
    /// </param>
    /// <param name="expectedKind">
    ///     Set when the slot's <c>referenceType</c> names an object KIND rather than a type. What
    ///     the slot accepts is then a predicate over the object's content, so the type name it
    ///     happens to carry says nothing - a planet declared as <c>&lt;My_Planet&gt;</c> is a planet.
    /// </param>
    /// <param name="index">
    ///     Needed to judge a kind, since behaviours come through the variant chain. Without it, and
    ///     for a predicate resting on facts the index does not hold, the reference goes unjudged:
    ///     an error on data we cannot read is the one outcome the author cannot act on.
    /// </param>
    public static (XmlDiagnosticSeverity Severity, string Message, DiagnosticId Id)? Evaluate(
        string targetId, string? expectedTypeName, GameSymbol? resolved,
        IReadOnlySet<string>? indexedTypeNames = null,
        ObjectKindDefinition? expectedKind = null, GameIndex? index = null)
    {
        // Scoped ability IDs are stored as "OWNER$name"; show only the bare name to the user.
        var displayId = StripOwnerPrefix(targetId);

        if (resolved is null)
        {
            // An EMPTY set means nothing is indexed yet - startup, or a fixture - not that this type
            // is unsupported. Claiming "not indexed yet" there would silence every genuine missing
            // reference until the index loads. Same guard PerFactionObjectListHandler applies to the
            // baseline before it trusts a faction lookup.
            if (expectedTypeName is not null && indexedTypeNames is { Count: > 0 }
                                             && !indexedTypeNames.Contains(expectedTypeName))
                return (XmlDiagnosticSeverity.Information,
                    $"Cannot verify reference '{displayId}': No {expectedTypeName} is indexed yet, "
                    + "so this reference cannot be checked.",
                    DiagnosticIds.ReferenceTypeNotIndexed);

            return (XmlDiagnosticSeverity.Error,
                $"Cannot resolve reference '{displayId}': No object with this name exists in the workspace.",
                DiagnosticIds.UnresolvedReference);
        }

        // A kind is judged by what the object IS, so it is answered here and the type name below is
        // never consulted: the index types a planet "GameObjectType" like everything else in its
        // file, and comparing that against "Planet" is the mismatch this whole change exists to
        // stop reporting.
        if (expectedKind is not null)
        {
            if (index is null || ObjectKinds.Match(index, resolved, expectedKind) != KindMatch.No)
                return null;

            var because = expectedKind.Behaviors.Count > 0
                ? $", but it has no {string.Join(" or ", expectedKind.Behaviors)} behaviour"
                : string.Empty;
            return (XmlDiagnosticSeverity.Error,
                $"Type mismatch for '{displayId}': Expected a {expectedKind.Kind}{because}.",
                DiagnosticIds.TypeMismatch);
        }

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
            $"Type mismatch for '{displayId}': Expected '{expectedTypeName}' but found '{resolved.TypeName}'.",
            DiagnosticIds.UnresolvedReference);
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