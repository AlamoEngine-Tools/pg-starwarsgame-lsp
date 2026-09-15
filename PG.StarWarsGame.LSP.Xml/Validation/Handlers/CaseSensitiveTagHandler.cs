// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A tag spelled differently from the only spelling its parser accepts.
/// </summary>
/// <remarks>
///     <para>
///         Severity follows the consequence rather than the rule. A mis-cased <c>Active_Plot</c>
///         changes what the game does - the plot loads suspended and the campaign never starts, with
///         nothing said at runtime - so it is an Error. A mis-cased <c>Suspended_Plot</c> reaches the
///         same outcome it would have anyway, so it is a Warning: worth knowing, not worth alarm.
///     </para>
///     <para>
///         The message names the exact spelling, because that is the entire fix and because "casing
///         matters here" is useless without saying which casing.
///     </para>
/// </remarks>
public sealed class CaseSensitiveTagHandler : XmlDiagnosticsHandler<CaseSensitiveTagFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.CaseSensitiveTag;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        CaseSensitiveTagFact fact, DiagnosticsContext ctx)
    {
        var severity = fact.ChangesBehaviour
            ? XmlDiagnosticSeverity.Error
            : XmlDiagnosticSeverity.Warning;

        return
        [
            new XmlDiagnosticResult(severity,
                $"<{fact.Authored}> has to be spelled exactly <{fact.Expected}>. Unlike the rest of "
                + $"the game's tags this one is matched letter by letter, so {fact.Consequence}.",
                SuggestedFix: fact.Expected,
                FixTitle: $"Spell it <{fact.Expected}>")
        ];
    }
}