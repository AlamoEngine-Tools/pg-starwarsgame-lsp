// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class ModelFileFormatHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != ReferenceKind.ModelFile)
            return [];

        var value = fact.RawValue;
        if (!value.EndsWith(".alo", StringComparison.OrdinalIgnoreCase))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{value}' is not a valid model filename for <{fact.Tag.Tag}>. Expected a .alo file.")
            ];

        return [];
    }
}