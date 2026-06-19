// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class VariantIgnoredOverrideHandler : XmlDiagnosticsHandler<VariantIgnoredOverrideFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(VariantIgnoredOverrideFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"Tag '{fact.TagName}' is ignored on variants; the engine will not apply it here.")
        ];
    }
}