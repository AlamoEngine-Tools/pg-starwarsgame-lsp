// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class UnitSpawnTableHandler : CommaSeparatedPairHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.UnitSpawnTable;

    protected override XmlValueType TargetType => XmlValueType.UnitSpawnTable;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        if (parts.Length != 2 || parts[0].Trim().Length == 0 ||
            !LenientIntParser.TryParse(parts[1].Trim(), out var count, out var wasFloat) || count < -1)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    ctx.Messages.Format("UnitSpawnTable_IsNotAValidUnit", fact.RawValue.Trim(), fact.Tag.Tag))
            ];

        // The unit (slot 0) is indexed as an object reference by XmlGameDocumentParser and its
        // existence is validated by the generic unresolved-reference pipeline (which also powers
        // go-to-definition/rename on it). This handler owns only the tuple shape and the count.
        if (!wasFloat)
            return [];

        // Consistent int-slot policy: floats are accepted (the game truncates) but warned.
        return
        [
            AtPairSlot(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    ctx.Messages.Format("UnitSpawnTable_IsAFloatButExpects", parts[1].Trim(), fact.Tag.Tag, count),
                    SuggestedFix: count.ToString(), Id: DiagnosticIds.UnitSpawnTableFloatWhereIntExpected),
                fact, 1)
        ];
    }
}