// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Base for rules about how MANY entries a list tag carries, as distinct from what each entry
///     looks like.
/// </summary>
/// <remarks>
///     <para>
///         An entry is <see cref="ValuesPerEntry" /> comma-separated values, so a curve written as
///         <c>0.0,0.0, 5.0,1.0</c> is two entries of two. Bounds are entries, not values, because
///         that is how the engine counts them - "must have at least two control points".
///     </para>
///     <para>
///         <see cref="MaximumEntries" /> of <c>null</c> means unlimited, and so does
///         <see cref="MinimumEntries" />. A subclass whose rule is not a simple interval - an exact
///         count, an even number, a multiple of something - overrides
///         <see cref="IsAcceptable" /> instead and the bounds are then only used for the message.
///     </para>
///     <para>
///         Shape is deliberately NOT checked here. The value type's own handler already reports a
///         ragged list and a mistyped token, and one mistake should not collect two diagnostics.
///     </para>
/// </remarks>
public abstract class ListLengthHandlerBase : XmlDiagnosticsHandler<XmlTagValueFact>, IXmlNamedDiagnosticsHandler
{
    /// <summary>Comma-separated values that make up one entry. A plain list leaves this at 1.</summary>
    protected virtual int ValuesPerEntry => 1;

    /// <summary>Fewest entries the engine accepts, or <c>null</c> for no lower bound.</summary>
    protected abstract int? MinimumEntries { get; }

    /// <summary>Most entries the engine accepts, or <c>null</c> for an unlimited list.</summary>
    protected abstract int? MaximumEntries { get; }

    /// <summary>The rule as a sentence fragment completing "&lt;Tag&gt; ...".</summary>
    protected abstract string Expectation { get; }

    /// <inheritdoc />
    public abstract string ValidationId { get; }

    /// <summary>
    ///     Whether a given entry count passes. Defaults to the interval; override for a rule the
    ///     interval cannot express.
    /// </summary>
    protected virtual bool IsAcceptable(int entries)
    {
        return (MinimumEntries is not { } min || entries >= min)
               && (MaximumEntries is not { } max || entries <= max);
    }

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var values = XmlUtility.SplitList(fact.RawValue).Count;

        // An empty tag is an omission rather than a miscounted list, and a ragged one is the type
        // handler's to report. Either way there is no meaningful entry count to judge.
        if (values == 0) return [];
        if (ValuesPerEntry > 1 && values % ValuesPerEntry != 0) return [];

        var entries = values / ValuesPerEntry;
        if (IsAcceptable(entries)) return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"<{fact.Tag.Tag}> {Expectation}, but has {entries}. "
                + "The engine rejects this value on load")
        ];
    }
}
