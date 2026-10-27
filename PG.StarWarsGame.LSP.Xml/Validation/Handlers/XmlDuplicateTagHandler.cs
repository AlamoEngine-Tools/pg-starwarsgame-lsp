// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class XmlDuplicateTagHandler : XmlDiagnosticsHandler<XmlDuplicateTagFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.XmlDuplicateTag;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlDuplicateTagFact fact, DiagnosticsContext ctx)
    {
        var othersText = ctx.Messages.Format("XmlDuplicateTag_OtherLines", string.Join(", ", fact.OtherLines));

        // Warning, not Error: the game loads the object top to bottom and the last occurrence
        // with a value wins, so duplicates technically work - they are just misleading style. The
        // other occurrences are dead values and get greyed out (Unnecessary); every occurrence
        // offers the "remove the duplicates the game ignores" quick fix.
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                ctx.Messages.Format("XmlDuplicateTag_DuplicateTagOnlyOneOccurrence", fact.Tag.Tag, othersText),
                Tags: fact.IsEffectiveOccurrence ? null : [XmlDiagnosticTag.Unnecessary],
                OfferRemoveEarlierDuplicates: true)
        ];
    }
}