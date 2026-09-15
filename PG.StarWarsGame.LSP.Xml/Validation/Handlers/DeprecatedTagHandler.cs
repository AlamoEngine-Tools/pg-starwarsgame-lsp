// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class DeprecatedTagHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.DeprecatedTag;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (!fact.Tag.Deprecated)
            return [];

        // The schema usually records WHY a tag was retired, in its own description - and "do not
        // use this" without a reason is the least actionable thing a diagnostic can say. Appended
        // rather than substituted, so the message still names the tag first.
        var message = $"<{fact.Tag.Tag}> is deprecated and should not be used.";
        if (fact.Tag.Description.TryGetValue(ctx.Locale, out var reason)
            && !string.IsNullOrWhiteSpace(reason))
            message += " " + reason.Trim();

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, message)];
    }
}