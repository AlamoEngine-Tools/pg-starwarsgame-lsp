// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     An empty (or <c>TBD</c>) occurrence of a tag that already has a value sets nothing: the game's
///     field mapper skips it, so the earlier or inherited value stays. Warning, because the game
///     runs - but an author who wrote the empty tag to clear the value has not, and reads it here.
/// </summary>
public sealed class XmlEmptyRepeatedTagHandler : XmlDiagnosticsHandler<XmlEmptyRepeatedTagFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.XmlEmptyRepeatedTag;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlEmptyRepeatedTagFact fact, DiagnosticsContext ctx)
    {
        var message = fact.KeptLine is { } line
            ? ctx.Messages.Format("XmlEmptyRepeatedTag_EarlierValueStays", fact.TagName, fact.KeptValue, line)
            : ctx.Messages.Format("XmlEmptyRepeatedTag_InheritedValueStays", fact.TagName, fact.KeptValue);

        // Dead text, greyed out like an earlier duplicate. Deliberately WITHOUT the remove-earlier
        // quick fix: that fix keeps the occurrence the game reads, which is not this one.
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, message, Tags: [XmlDiagnosticTag.Unnecessary])
        ];
    }
}