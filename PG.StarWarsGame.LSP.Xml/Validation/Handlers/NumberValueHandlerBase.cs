// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Base for numeric-type handlers. Applies a lenient float gate first:
///     if the raw value is not a valid float, it emits an Error.
///     Otherwise it delegates to <see cref="HandlePrecise" /> with the parsed float value
///     so the subclass can emit a Warning with a suggested corrected value when the
///     more-precise constraint (e.g. must be an integer, must be in range) is not met.
/// </summary>
public abstract class NumberValueHandlerBase : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected abstract XmlValueType TargetType { get; }

    public override XmlValueType? HandledValueType => TargetType;

    protected sealed override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != TargetType)
            return [];

        var trimmed = fact.RawValue.Trim();
        if (!LenientFloatParser.TryParse(trimmed, out var floatVal))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid number for <{fact.Tag.Tag}>.")
            ];

        return HandlePrecise(fact, trimmed, floatVal, ctx);
    }

    /// <summary>
    ///     Called when the lenient float parse succeeded.
    ///     Return an empty enumerable when the precise constraint is satisfied,
    ///     a Warning with <see cref="XmlDiagnosticResult.SuggestedFix" /> when the value
    ///     is close (e.g. a float in an int field), or an Error when it is truly invalid.
    /// </summary>
    protected abstract IEnumerable<XmlDiagnosticResult> HandlePrecise(
        XmlTagValueFact fact, string trimmed, double floatVal, DiagnosticsContext ctx);
}