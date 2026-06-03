// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class DeprecatedTagHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (!fact.Tag.Deprecated)
            return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"<{fact.Tag.Tag}> is deprecated and should not be used.")
        ];
    }
}