// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class NormalizedFloatHandler : SingleValueTypeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.NormalizedFloat;

    protected override XmlValueType TargetType => XmlValueType.NormalizedFloat;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (!EngineNumber.TryParseDouble(fact.RawValue, out var d))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    ctx.Messages.Format("NormalizedFloat_IsNotAValidNumber", fact.RawValue.Trim(), fact.Tag.Tag))
            ];

        if (d is < 0.0 or > 1.0)
        {
            var clamped = Math.Clamp(d, 0.0, 1.0).ToString("G", CultureInfo.InvariantCulture);
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    ctx.Messages.Format("NormalizedFloat_ValueIsOutOfRange", d, fact.Tag.Tag, clamped),
                    SuggestedFix: clamped, Id: DiagnosticIds.NormalizedFloatOutOfRange)
            ];
        }

        return [];
    }
}