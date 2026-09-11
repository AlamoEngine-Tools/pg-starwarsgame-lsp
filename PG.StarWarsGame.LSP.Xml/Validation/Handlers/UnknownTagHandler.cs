// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A tag the schema does not know is reported as a warning: the engine reads it, finds no
///     parser row for it, and discards the value.
/// </summary>
/// <remarks>
///     <para>
///         Warning rather than error, and deliberately easy to silence, for two reasons. Mods carry
///         tags meant for external tools that the engine was never supposed to read; and the shipped
///         data is full of abandoned ones - 458 distinct names over 3800 occurrences across
///         <c>foc/</c> and <c>eaw/</c>, measured at this rule's exact scope. Every one of those was
///         checked against the engine's own parser table and none has a row, so the count is loud
///         but not wrong.
///     </para>
///     <para>
///         Not an error, because being unknown to US is weaker evidence than being unknown to the
///         engine: a tag added after the build we mapped would look identical from here.
///     </para>
/// </remarks>
public sealed class UnknownTagHandler : XmlDiagnosticsHandler<XmlUnknownTagFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.UnknownTag;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        XmlUnknownTagFact fact, DiagnosticsContext ctx)
    {
        var message = fact.Suggestion is null
            ? $"<{fact.TagName}> is not a known {fact.OwnerElement} tag - the engine ignores it"
            : $"<{fact.TagName}> is not a known {fact.OwnerElement} tag - did you mean " +
              $"<{fact.Suggestion}>?";

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, message)];
    }
}
