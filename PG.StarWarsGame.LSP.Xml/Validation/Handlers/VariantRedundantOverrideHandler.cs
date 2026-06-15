// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class VariantRedundantOverrideHandler : XmlDiagnosticsHandler<VariantRedundantOverrideFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(VariantRedundantOverrideFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint,
                $"Tag '{fact.TagName}' has the same value as the inherited base; this override is redundant.",
                Tags: [XmlDiagnosticTag.Unnecessary],
                RemoveRedundantOverride: true)
        ];
    }
}
