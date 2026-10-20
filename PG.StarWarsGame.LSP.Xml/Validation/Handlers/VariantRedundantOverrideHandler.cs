// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class VariantRedundantOverrideHandler : XmlDiagnosticsHandler<VariantRedundantOverrideFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.VariantRedundantOverride;

    protected override IEnumerable<XmlDiagnosticResult> Handle(VariantRedundantOverrideFact fact,
        DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint,
                ctx.Messages.Format("VariantRedundantOverride_TagHasTheSameValue", fact.TagName),
                Tags: [XmlDiagnosticTag.Unnecessary],
                RemoveRedundantOverride: true)
        ];
    }
}