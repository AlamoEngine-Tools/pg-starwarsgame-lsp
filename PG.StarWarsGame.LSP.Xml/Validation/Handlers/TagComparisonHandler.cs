// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports two tags that do not hold the relation the engine requires between them.
/// </summary>
/// <remarks>
///     A warning rather than an error: the document is well formed and the game will load it. The
///     engine complains at load and the ability does not behave, which is a configuration mistake
///     rather than a broken definition.
/// </remarks>
public sealed class TagComparisonHandler : XmlDiagnosticsHandler<TagComparisonFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.TagComparison;

    protected override IEnumerable<XmlDiagnosticResult> Handle(TagComparisonFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"<{fact.LeftTag}> {fact.Expectation} <{fact.RightTag}>, "
                + $"but is {Show(fact.Left)} against {Show(fact.Right)}")
        ];
    }

    private static string Show(double value)
    {
        return value.ToString("G", CultureInfo.InvariantCulture);
    }
}
