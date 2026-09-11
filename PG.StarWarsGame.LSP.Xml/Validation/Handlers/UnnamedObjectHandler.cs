// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     An object with no usable name is reported as an error, not a warning: it is not a style
///     problem but content the game and the tooling both drop on the floor.
/// </summary>
/// <remarks>
///     Safe at error severity because it cannot fire on correct data. Measured over the 872 shipped
///     XML files in <c>foc/</c> and <c>eaw/</c>: 44 contain the text <c>Name=""</c>, and every one
///     of those sits inside a comment block, so the live count is zero.
/// </remarks>
public sealed class UnnamedObjectHandler : XmlDiagnosticsHandler<XmlUnnamedObjectFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.UnnamedObject;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        XmlUnnamedObjectFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<{fact.ElementName}> has no {fact.NameTag} - it cannot be referenced or " +
                $"overridden, and is ignored as a {fact.TypeName}.")
        ];
    }
}
