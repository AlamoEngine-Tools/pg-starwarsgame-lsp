// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     An ability with both halves of an either/or flag pair off.
/// </summary>
/// <remarks>
///     Error, though the engine labels one of the three Warning and the other two Error while
///     stating the same consequence in the same words. The outcome is what settles it here: the
///     object loads, the ability is inert, and nothing in the game says so - the author finds out
///     by wondering why an ability they paid for never fires.
/// </remarks>
public sealed class EitherOrRequirementHandler : XmlDiagnosticsHandler<EitherOrRequirementFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.EitherOrRequirement;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        EitherOrRequirementFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<{fact.FirstTag}> and <{fact.SecondTag}> are both off, so this "
                + $"{fact.OwningType} does nothing - set one of them to Yes")
        ];
    }
}
