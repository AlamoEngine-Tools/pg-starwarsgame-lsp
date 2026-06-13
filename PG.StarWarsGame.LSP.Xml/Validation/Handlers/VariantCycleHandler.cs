// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class VariantCycleHandler : XmlDiagnosticsHandler<VariantCycleFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(VariantCycleFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"Variant '{fact.ObjectId}' has a circular inheritance chain (loops at '{fact.CycleObjectId}').")
        ];
    }
}
