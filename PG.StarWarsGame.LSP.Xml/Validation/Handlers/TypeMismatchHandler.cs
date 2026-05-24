// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class TypeMismatchHandler : XmlDiagnosticsHandler<XmlReferenceFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlReferenceFact fact, DiagnosticsContext ctx)
    {
        if (fact.Resolved is null || fact.ExpectedTypeName is null)
            return [];

        if (string.Equals(fact.ExpectedTypeName, "GameObjectType", StringComparison.OrdinalIgnoreCase))
            return [];

        if (fact.Resolved.TypeName == fact.ExpectedTypeName)
            return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"Type mismatch for '{fact.TargetId}': expected '{fact.ExpectedTypeName}' but found '{fact.Resolved.TypeName}'.")
        ];
    }
}