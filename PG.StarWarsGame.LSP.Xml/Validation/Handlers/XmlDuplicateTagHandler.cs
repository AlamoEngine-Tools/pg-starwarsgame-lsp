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

        // Warning, not Error: the game loads the object top to bottom and the LAST occurrence
        // wins, so duplicates technically work - they are just misleading style. Earlier
        // occurrences are dead values and get greyed out (Unnecessary); every occurrence offers
        // the "remove earlier duplicates" quick fix.
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"Duplicate tag '{fact.Tag.Tag}': only one occurrence is used per object - the game keeps the LAST one.{othersText}",
                Tags: fact.IsLastOccurrence ? null : [XmlDiagnosticTag.Unnecessary],
                OfferRemoveEarlierDuplicates: true)
        ];
    }
}