// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Base for the range rules the engine states about its numeric tags: an optional lower bound,
///     an optional upper bound, and whether each is inclusive.
/// </summary>
/// <remarks>
///     <para>
///         Every range the engine spells out fits these four knobs - "cannot be less than zero" is
///         an inclusive minimum of 0, "must be greater than zero" the same bound made exclusive,
///         "cannot be -1.0 or less" an exclusive minimum of -1, "must be between 0 and 180" a
///         closed interval, and "must be &gt;= 0.0 and &lt; 1.0" a half-open one. A subclass states
///         the bounds and the sentence; nothing else varies.
///     </para>
///     <para>
///         Subclasses opt in per tag by <c>validationId</c>, so no <c>XmlValueType</c> member is
///         invented and each tag keeps the numeric type the engine actually parses.
///     </para>
/// </remarks>
public abstract class NumericRangeHandlerBase : XmlDiagnosticsHandler<XmlTagValueFact>, IXmlNamedDiagnosticsHandler
{
    /// <summary>Lower bound, or <c>null</c> where the engine states none.</summary>
    protected abstract float? Minimum { get; }

    /// <summary>Upper bound, or <c>null</c> where the engine states none.</summary>
    protected abstract float? Maximum { get; }

    /// <summary>Whether <see cref="Minimum" /> is itself allowed. Most ranges include it.</summary>
    protected virtual bool MinimumInclusive => true;

    /// <summary>Whether <see cref="Maximum" /> is itself allowed.</summary>
    protected virtual bool MaximumInclusive => true;

    /// <summary>
    ///     The rule as a sentence fragment, phrased as the engine phrases it, completing
    ///     "&lt;Tag&gt; ...".
    /// </summary>
    protected abstract string Expectation { get; }

    /// <inheritdoc />
    public abstract string ValidationId { get; }

    /// <summary>
    ///     What the engine assigns after complaining, where that has been READ OUT OF THE BINARY -
    ///     the assignment following the message. Offered as a quick fix so the file can be made to
    ///     say what the game will do with it anyway.
    /// </summary>
    /// <remarks>
    ///     Null by default, and it must stay null until measured. The repair is not implied by the
    ///     bound: a rule may clamp to the bound, clamp to something inside it (the income split
    ///     clamps to 0.99, not 1.0), reset to zero, or clear the value entirely. Guessing here would
    ///     put the author's file one keystroke from a value the engine never picks - and where a
    ///     handler serves several owners, the repair can differ per owner, which is a reason to
    ///     leave it null rather than to average it.
    /// </remarks>
    protected virtual string? RepairValue => null;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        // A value that is not a number belongs to the type handler; a range check has nothing to
        // say about it, and complaining twice about one mistake helps nobody.
        if (!LenientFloatParser.TryParse(fact.RawValue.Trim(), out var value))
            return [];

        var belowMinimum = Minimum is { } min && (MinimumInclusive ? value < min : value <= min);
        var aboveMaximum = Maximum is { } max && (MaximumInclusive ? value > max : value >= max);

        if (!belowMinimum && !aboveMaximum)
            return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"<{fact.Tag.Tag}> {Expectation}. The engine rejects this value on load",
                SuggestedFix: RepairValue,
                FixTitle: RepairValue is null ? null : $"Apply the engine's own value: {RepairValue}")
        ];
    }
}
