// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Text at or near the fixed buffer the engine copies it into.
/// </summary>
/// <remarks>
///     <para>
///         Error over the limit, and the most consequential rule here: the copy is a bare
///         <c>strcpy</c> into a stack buffer, so exceeding it does not truncate the value or refuse
///         the object - it corrupts the stack during load and the game does not start.
///     </para>
///     <para>
///         Warning while approaching it, because these limits are reached by ACCUMULATION - another
///         hardpoint, another planet, a rename from <c>HP01</c> to something readable. By the time
///         it is crossed, the edit that crossed it is rarely the edit that caused it.
///     </para>
///     <para>
///         <b>The message talks in characters, never bytes.</b> Bytes are what the engine counts and
///         what we measure against; they are no use to someone editing a list, who can only add or
///         delete characters. Where the two diverge the character figure is computed rather than
///         converted - 809 bytes over is 270 characters when the text is three-byte, and saying 809
///         would have them delete three times what they need to.
///     </para>
/// </remarks>
public sealed class EngineTextLimitHandler : XmlDiagnosticsHandler<EngineTextLimitFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.EngineTextLimit;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        EngineTextLimitFact fact, DiagnosticsContext ctx)
    {
        if (fact.CharactersOver > 0)
        {
            var over = Show(fact.CharactersOver);

            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"{fact.Subject} is {over} characters too long for the engine, and the game will "
                    + $"not start. Remove at least {over} characters, or shorten the names inside it.")
            ];
        }

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"{fact.Subject} has room for {Show(fact.CharactersLeft)} more characters. Going "
                + "over stops the game from starting.",
                Id: DiagnosticIds.EngineTextLimitApproaching)
        ];
    }

    private static string Show(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}