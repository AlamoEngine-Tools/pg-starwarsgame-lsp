// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Validates a <see cref="XmlValueType.ProjectileCategoryList" /> tag - a comma-separated list
///     of <c>ProjectileCategory</c> values.
/// </summary>
/// <remarks>
///     <para>
///         One tag carries this type: <c>Projectile_Types_Targeted</c> on
///         <c>Laser_Defense_Ability</c> (engine code <c>0x51</c> at <c>017f5020</c>), the list of
///         projectile categories a point-defence laser will shoot down.
///     </para>
///     <para>
///         Worth checking because the failure is invisible. A misspelt category does not fail the
///         load - it just never matches, so the point defence quietly ignores that projectile and
///         the only symptom is a turret that does not fire at missiles.
///     </para>
///     <para>
///         Same shape as <see cref="VictoryConditionListHandler" />, including the two rules that
///         matter: tokens are trimmed, because the list is conventionally written one per line; and
///         a tag with no enum wired is left unchecked rather than measured against the wrong set.
///     </para>
/// </remarks>
public sealed class ProjectileCategoryListHandler : NamedEnumValueHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ProjectileCategoryList;

    protected override XmlValueType TargetType => XmlValueType.ProjectileCategoryList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var valid = GetValidValues(fact.Tag.Enum, ctx);
        if (valid is null)
            return [];

        foreach (var token in fact.RawValue.Split(',',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (!valid.Contains(token))
                return
                [
                    new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        $"'{token}' is not a known {fact.Tag.Enum!.Name} on <{fact.Tag.Tag}> - " +
                        "the engine never matches it, so that projectile is not intercepted.")
                ];

        return [];
    }
}
