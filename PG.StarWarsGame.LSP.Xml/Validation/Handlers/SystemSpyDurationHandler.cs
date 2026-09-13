// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A system spy's duration on the wrong side of zero for its activation style.
/// </summary>
/// <remarks>
///     Error, and the engine agrees for once - it labels both arms <c>Error:</c>. The outcome is a
///     silent behaviour change either way: a galactic spy the file gives 30 seconds runs forever,
///     and a ground-activated one given a negative duration runs for 30 seconds instead. The fix is
///     anchored on the value and writes what the engine writes, so accepting it changes nothing
///     about the game and only makes the file agree with it.
/// </remarks>
public sealed class SystemSpyDurationHandler : XmlDiagnosticsHandler<SystemSpyDurationFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.SystemSpyDurationSign;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        SystemSpyDurationFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<Duration_In_Secs> is {fact.Duration.ToString("G", CultureInfo.InvariantCulture)}, "
                + $"but a {fact.Style} system spy needs a duration {fact.Requirement} - the engine "
                + $"overwrites it with {fact.Repair}",
                SuggestedFix: fact.Repair,
                FixTitle: $"Apply the engine's own value: {fact.Repair}")
        ];
    }
}
