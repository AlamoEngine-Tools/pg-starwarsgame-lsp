// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Validates an engine type-69 tag - a comma-separated list of victory-condition enum values
///     (<c>Campaign</c>'s <c>Good_/Evil_/Human_/AI_Victory_Conditions</c>). Each token must be a
///     member of the tag's schema-fixed enum (<c>GalacticVictoryCondition</c>); an unknown token is
///     an error. Items are conventionally written one per line, so a token's surrounding whitespace
///     (including newlines) is trimmed before the lookup.
/// </summary>
public sealed class VictoryConditionListHandler : NamedEnumValueHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.Type69;

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
