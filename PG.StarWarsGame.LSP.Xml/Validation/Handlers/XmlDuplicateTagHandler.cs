// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class XmlDuplicateTagHandler : XmlDiagnosticsHandler<XmlDuplicateTagFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlDuplicateTagFact fact, DiagnosticsContext ctx)
    {
        var othersText = fact.OtherLines.Count == 1
            ? $" Also at line {fact.OtherLines[0]}."
            : $" Also at lines {string.Join(", ", fact.OtherLines)}.";

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"Duplicate tag '{fact.Tag.Tag}': only one occurrence is allowed per object.{othersText}")
        ];
    }
}