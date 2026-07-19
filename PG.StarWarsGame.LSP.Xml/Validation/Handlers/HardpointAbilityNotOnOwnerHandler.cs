// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a <c>Special_Ability_Name</c> the mounting object does not have. Error: the resolution
///     is deterministic - it walks the object's own variant chain and needs no model or asset data - and
///     the consequence is an ability that silently never fires.
/// </summary>
public sealed class HardpointAbilityNotOnOwnerHandler
    : XmlDiagnosticsHandler<HardpointAbilityNotOnOwnerFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointAbilityNotOnOwnerFact fact, DiagnosticsContext ctx)
    {
        // Naming the two cases separately matters: one is a typo, the other is an ability sitting on
        // the wrong unit, and they are fixed in different files.
        var tail = fact.DefinedElsewhere
            ? $"'{fact.AbilityName}' is defined, but on a different object - it has to be declared on "
              + $"'{fact.OwnerId}' (or something it inherits from) to take effect here."
            : $"No object declares an ability called '{fact.AbilityName}'.";

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"Hardpoint '{fact.HardpointId}' enables ability '{fact.AbilityName}' on '{fact.OwnerId}', "
                + $"which does not have it. {tail}")
        ];
    }
}
