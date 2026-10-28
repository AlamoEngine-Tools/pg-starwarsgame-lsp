// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a hardpoint's unit-mode row that its attaching object can never put to use. A
///     warning: the hardpoint fires as if the row were not there, which is what the author did
///     not intend, but nothing breaks.
/// </summary>
public sealed class HardpointModeRowWithoutAbilityHandler
    : XmlDiagnosticsHandler<HardpointModeRowWithoutAbilityFact>
{
    private const string RocketRowTag = "Fire_When_In_Rocket_Attack_Mode";

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.HardpointModeRowWithoutAbility;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointModeRowWithoutAbilityFact fact, DiagnosticsContext ctx)
    {
        var slots = fact.OwnerAbilities.Count == 0
            ? ctx.Messages.Format("HardpointModeRowWithoutAbility_NoAbility")
            : ctx.Messages.Format("HardpointModeRowWithoutAbility_Abilities", string.Join(", ", fact.OwnerAbilities));

        // Two causes, two remedies: add the ability, or move it up the list. The rocket row knows no
        // slots: the game keeps an ability's state by its type, from every entry of the list.
        var tail = fact.SlotOfType >= 0
            ? ctx.Messages.Format("HardpointModeRowWithoutAbility_SlotTooLate", fact.OwnerId, fact.AbilityType,
                fact.SlotOfType + 1, slots)
            : fact.TagName.Equals(RocketRowTag, StringComparison.OrdinalIgnoreCase)
                ? ctx.Messages.Format("HardpointModeRowWithoutAbility_NotOnUnit", fact.OwnerId, slots,
                    fact.AbilityType)
                : ctx.Messages.Format("HardpointModeRowWithoutAbility_NotInSlots", fact.OwnerId, slots,
                    fact.AbilityType);

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                ctx.Messages.Format("HardpointModeRowWithoutAbility_HardpointSetsWhichNeverApplies", fact.HardpointId,
                    fact.TagName, tail))
        ];
    }
}