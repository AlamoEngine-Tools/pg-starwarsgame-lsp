// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     The owner's share of a split income, outside the range the engine accepts.
/// </summary>
/// <remarks>
///     The fix writes the value the engine picks - 0.99 at the top, not 1.0 - so accepting it
///     changes nothing about the game and only makes the file agree with it. Anchored on the value,
///     so the ordinary replace-this-range quick fix applies.
/// </remarks>
public sealed class OwnerIncomeShareHandler : XmlDiagnosticsHandler<OwnerIncomeShareFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.OwnerIncomeShareOutOfRange;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        OwnerIncomeShareFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<Owner_Income_Percentage> is {fact.Value.ToString("G", CultureInfo.InvariantCulture)}, "
                + "but a split that favours the owner needs at least 0 and less than 1 - the engine "
                + $"clamps it to {fact.Clamped}",
                SuggestedFix: fact.Clamped,
                FixTitle: $"Apply the engine's own value: {fact.Clamped}")
        ];
    }
}

/// <summary>
///     A flag combination on an income stream that the engine clears at load.
/// </summary>
/// <remarks>
///     Error rather than warning even though the engine labels both of these <c>Warning</c>: the
///     outcome is a silent behaviour change - the ability ships splitting its income differently
///     from what the file says, and nothing in the game mentions it.
/// </remarks>
public sealed class IncomeSplitConflictHandler : XmlDiagnosticsHandler<IncomeSplitConflictFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.IncomeSplitConflict;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        IncomeSplitConflictFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<{fact.FlagTag}> {fact.Problem} - the engine {fact.Consequence}",
                EngineRepair: fact.Repair)
        ];
    }
}