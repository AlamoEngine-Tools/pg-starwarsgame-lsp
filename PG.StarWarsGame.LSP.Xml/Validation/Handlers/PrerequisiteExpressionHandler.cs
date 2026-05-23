// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Validates the boolean prerequisite expression format used by Required_Special_Structures.
///     Only handles tags with ValueType = GameObjectTypeReferenceList AND SemanticType = PrerequisiteExpression.
/// </summary>
public sealed partial class PrerequisiteExpressionHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.GameObjectTypeReferenceList ||
            fact.Tag.SemanticType != TagSemanticType.PrerequisiteExpression)
            return [];

        var trimmed = fact.RawValue.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"<{fact.Tag.Tag}> expects a prerequisite expression; value must not be empty.")
            ];

        if (!ExpressionPattern().IsMatch(trimmed))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid prerequisite expression for <{fact.Tag.Tag}>. " +
                    "Expected game object names connected by '|' (OR) and ',' or spaces (AND). " +
                    "Example: \"StructA | StructB, StructC\" means (StructA OR StructB) AND StructC.")
            ];

        return [];
    }

    [GeneratedRegex(@"^\w+(\s*[|,]\s*\w+|\s+\w+)*$")]
    private static partial Regex ExpressionPattern();
}