// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class IntValueHandler : NumberValueHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.IntValue;

    protected override XmlValueType TargetType => XmlValueType.Int;

    protected override IEnumerable<XmlDiagnosticResult> HandlePrecise(
        XmlTagValueFact fact, string trimmed, double floatVal, DiagnosticsContext ctx)
    {
        if (int.TryParse(trimmed, out _))
            return [];

        if (floatVal is >= int.MinValue and <= int.MaxValue)
        {
            var corrected = ((int)floatVal).ToString();
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    ctx.Messages.Format("IntValue_IsAFloatButExpects", trimmed, fact.Tag.Tag, corrected),
                    SuggestedFix: corrected, Id: DiagnosticIds.IntValueFloatWhereIntExpected)
            ];
        }

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                ctx.Messages.Format("IntValue_IsOutOfRangeFor", trimmed, fact.Tag.Tag),
                Id: DiagnosticIds.IntValueOutOfRange)
        ];
    }
}