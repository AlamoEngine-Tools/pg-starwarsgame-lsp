// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Validates an <see cref="XmlValueType.EnumValueList" /> tag - a comma-separated list of enum
///     values. Each token must be a member of the enum the tag names; an unknown token is an error.
///     Items are conventionally written one per line, so a token's surrounding whitespace
///     (including newlines) is trimmed before the lookup.
/// </summary>
/// <remarks>
///     Despite the name, this is not specific to victory conditions. The engine gives the same type
///     to <c>Campaign</c>'s <c>Good_/Evil_/Human_/AI_Victory_Conditions</c>
///     (<c>GalacticVictoryCondition</c>) and to <c>TargetingPrioritySet</c>'s
///     <c>Hard_Point_Priorities</c> and <c>Hard_Point_Exclusions</c>, which carry a different
///     vocabulary. The lookup has always come from <c>fact.Tag.Enum</c>, so the behaviour was
///     already general; a tag with no enum wired is left unchecked rather than measured against the
///     wrong set.
/// </remarks>
public sealed class VictoryConditionListHandler : NamedEnumValueHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.VictoryConditionList;

    protected override XmlValueType TargetType => XmlValueType.EnumValueList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var valid = GetValidValues(fact.Tag.Enum, ctx);
        if (valid is null)
            return []; // no enum wired / open-world - nothing to check

        foreach (var token in fact.RawValue.Split(',',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (!valid.Contains(token))
                return
                [
                    new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        $"'{token}' is not a known value for enum '{fact.Tag.Enum!.Name}' on <{fact.Tag.Tag}>.")
                ];

        return [];
    }
}
