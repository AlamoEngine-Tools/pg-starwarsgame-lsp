// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a declared damage stage the object's model has nothing tagged for.
/// </summary>
/// <remarks>
///     Warning rather than error: the stage is legal and the game runs: what it costs is the visible
///     change at that state, so the unit takes damage and still looks untouched. Measured across foc,
///     6 of the 219 objects declaring the table hit this.
/// </remarks>
public sealed class DamageStageNotOnModelHandler
    : XmlDiagnosticsHandler<DamageStageNotOnModelFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.DamageStageNotOnModel;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        DamageStageNotOnModelFact fact, DiagnosticsContext ctx)
    {
        var which = fact.MissingStages.Count == 1
            ? $"stage {fact.MissingStages[0]}"
            : $"stages {string.Join(", ", fact.MissingStages)}";

        // What the model DOES tag, so the reader can see whether this is one stage missed or a model
        // that stages nothing at all - the two want different fixes.
        var has = fact.TaggedStages.Count == 0
            ? "tags no damage stage at all"
            : $"tags only {string.Join(", ", fact.TaggedStages)}";

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"<Land_Damage_Alternates> declares damage {which}, but '{fact.ModelName}' {has}. "
                + "The unit reaches that state and does not change.")
        ];
    }
}
