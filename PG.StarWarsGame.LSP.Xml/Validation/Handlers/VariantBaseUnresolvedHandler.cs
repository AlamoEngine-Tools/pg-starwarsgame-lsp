// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A variant base that does not resolve is an error: nothing is inherited and nothing says so.
/// </summary>
/// <remarks>
///     Error rather than warning because the engine gives the author no other signal at all. A
///     cycle at least gets reported by this tool; an unresolvable base is the one variant failure
///     with no assert, no log line, and no visible symptom beyond the unit behaving like a blank
///     slate. The usual causes are a typo in the base name, a base living in a file that is not
///     loaded, or the base itself failing to resolve and taking its variants down with it.
/// </remarks>
public sealed class VariantBaseUnresolvedHandler : XmlDiagnosticsHandler<VariantBaseUnresolvedFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.VariantBaseUnresolved;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        VariantBaseUnresolvedFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"Variant base '{fact.BaseId}' does not resolve - the engine ignores the tag " +
                $"silently, so '{fact.ObjectId}' inherits nothing.")
        ];
    }
}
