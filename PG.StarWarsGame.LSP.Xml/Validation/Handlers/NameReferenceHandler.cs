// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class NameReferenceHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.NameReference;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (trimmed.Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'' is not a valid name reference for <{fact.Tag.Tag}>.")
            ];

        if (fact.Tag.ReferenceKind == ReferenceKind.HardcodedSet && fact.Tag.HardcodedSet is { } set)
        {
            var known = set.Values.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!known.Contains(trimmed))
                return
                [
                    new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        $"'{trimmed}' is not a known value for '{set.Name}' on <{fact.Tag.Tag}>.")
                ];
        }

        return [];
    }
}