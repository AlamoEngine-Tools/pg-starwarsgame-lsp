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
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.HardpointModeRowWithoutAbility;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointModeRowWithoutAbilityFact fact, DiagnosticsContext ctx)
    {
        var slots = fact.OwnerAbilities.Count == 0
            ? "no unit ability"
            : $"unit abilities {string.Join(", ", fact.OwnerAbilities)}";

        // Two causes, two remedies: add the ability, or move it up the list.
        var tail = fact.SlotOfType < 0
            ? $"'{fact.OwnerId}' has {slots}; the game reads the row only while an active " +
              $"{fact.AbilityType} ability sits in its first or second slot."
            : $"'{fact.OwnerId}' has {fact.AbilityType} in slot {fact.SlotOfType + 1} of {slots}; " +
              "the game reads the row for the first two slots only.";

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                ctx.Messages.Format("HardpointModeRowWithoutAbility_HardpointSetsWhichNeverApplies", fact.HardpointId,
                    fact.TagName, tail))
        ];
    }
}