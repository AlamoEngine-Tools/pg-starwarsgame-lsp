// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Enforces <see cref="XmlTagDefinition.AllowedValues" /> - the subset of a tag's values that
///     ONE owning type accepts.
/// </summary>
/// <remarks>
///     <para>
///         Several ability classes accept exactly one value for a tag and say so in their own
///         <c>Validate_Data</c>: nine name a single <c>Activation_Style</c>, and
///         <c>GalacticSabotageAbility</c> demands <c>Causes_Despawn</c> be Yes. In every case the
///         engine REPAIRS the object rather than refusing it, so the ability runs on a value the
///         author did not write and nothing on screen says so.
///     </para>
///     <para>
///         Deliberately NOT inside a value-type handler. It started in
///         <see cref="DynamicEnumValueHandler" /> and that was wrong as soon as a Boolean needed
///         the same restriction - the field lives on the tag, so the check belongs wherever a tag
///         value arrives, not in one type's handler.
///     </para>
/// </remarks>
public sealed class AllowedValuesHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.EnumValueNotAllowedHere;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var allowed = fact.Tag.AllowedValues;
        if (allowed.Count == 0) return [];

        var value = fact.RawValue.Trim();
        if (value.Length == 0) return [];

        if (IsAllowed(fact.Tag, value, allowed)) return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<{fact.Tag.Tag}> here only supports {Listed(allowed)} - the engine overwrites " +
                "anything else.")
        ];
    }

    private static bool IsAllowed(XmlTagDefinition tag, string value, IReadOnlyList<string> allowed)
    {
        // A boolean's spellings are interchangeable to the engine, so compare what the value MEANS.
        // An unrecognised spelling is not a subset violation - BooleanValueHandler reports it, and
        // one typo should not collect two diagnostics.
        if (tag.ValueType == XmlValueType.Boolean)
            return !EngineBoolean.IsValid(value) ||
                   allowed.Any(a => EngineBoolean.IsTrue(a) == EngineBoolean.IsTrue(value));

        return allowed.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Renders an allowed-value set as prose: "X", or "X or Y", or "X, Y or Z".</summary>
    private static string Listed(IReadOnlyList<string> values)
    {
        if (values.Count == 1) return values[0];
        return string.Join(", ", values.Take(values.Count - 1)) + " or " + values[^1];
    }
}
