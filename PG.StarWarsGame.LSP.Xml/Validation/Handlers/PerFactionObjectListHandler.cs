// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class PerFactionObjectListHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.PerFactionObjectList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        var parts = trimmed.Split(',');
        // Need at least FactionName + one ObjectName; all elements must be non-empty
        if (parts.Length < 2 || parts.Any(p => p.Trim().Length == 0))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid per-faction object list for <{fact.Tag.Tag}>. Expected: FactionName, ObjectName[, ...].")
            ];

        return [];
    }
}