// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a tag the owning type cannot work without.
/// </summary>
/// <remarks>
///     A warning rather than an error: the document is well formed and the game will load it. The
///     ability simply will not run, and the engine says so at load rather than refusing the file.
/// </remarks>
public sealed class MissingRequiredTagHandler : XmlDiagnosticsHandler<MissingRequiredTagFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.MissingRequiredTag;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        MissingRequiredTagFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"<{fact.TagName}> is not set. {fact.OwningType} needs it, and the engine reports "
                + "it as unset on load")
        ];
    }
}
