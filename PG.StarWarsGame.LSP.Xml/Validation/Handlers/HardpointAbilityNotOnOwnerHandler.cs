// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a <c>Special_Ability_Name</c> the attaching object does not have. Error: the resolution
///     is deterministic - it walks the object's own variant chain and needs no model or asset data - and
///     the consequence is an ability that silently never fires.
/// </summary>
public sealed class HardpointAbilityNotOnOwnerHandler
    : XmlDiagnosticsHandler<HardpointAbilityNotOnOwnerFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.HardpointAbilityNotOnOwner;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointAbilityNotOnOwnerFact fact, DiagnosticsContext ctx)
    {
        // Naming the two cases separately matters: one is a typo, the other is an ability sitting on
        // the wrong unit, and they are fixed in different files.
        var tail = fact.DefinedElsewhere
            ? ctx.Messages.Format("HardpointAbilityNotOnOwner_DefinedElsewhere", fact.AbilityName, fact.OwnerId)
            : ctx.Messages.Format("HardpointAbilityNotOnOwner_NotDeclared", fact.AbilityName);

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                ctx.Messages.Format("HardpointAbilityNotOnOwner_HardpointEnablesAbilityOnWhich", fact.HardpointId,
                    fact.AbilityName, fact.OwnerId, tail))
        ];
    }
}