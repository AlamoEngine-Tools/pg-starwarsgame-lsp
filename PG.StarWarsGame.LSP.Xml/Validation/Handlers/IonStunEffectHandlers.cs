// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports the ion stun effects a type without the ION_STUN_EFFECT behaviour draws from the start.
/// </summary>
/// <remarks>
///     Information: a state of the type, named, with nothing implied about whether it is meant. A
///     wreck that sparks for as long as it lies there is the same finding as a ship that was meant
///     to spark only while stunned.
/// </remarks>
public sealed class IonEffectsNeverHiddenHandler : XmlDiagnosticsHandler<IonEffectsNeverHiddenFact>
{
    // Enough to find them in the model by; a hull of dozens of sparks would otherwise fill the hover.
    private const int NamedEffects = 8;

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.IonEffectsNeverHidden;

    protected override IEnumerable<XmlDiagnosticResult> Handle(IonEffectsNeverHiddenFact fact, DiagnosticsContext ctx)
    {
        var named = string.Join(", ", fact.Effects.Take(NamedEffects));
        var effects = fact.Effects.Count > NamedEffects
            ? ctx.Messages.Format("IonEffectsNeverHidden_AndMore", named, fact.Effects.Count - NamedEffects)
            : named;

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Information,
                ctx.Messages.Format("IonEffectsNeverHidden_DrawnFromTheStart", fact.ModelName, effects))
        ];
    }
}