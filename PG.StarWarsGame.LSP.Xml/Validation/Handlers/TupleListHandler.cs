// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     The default check for <see cref="XmlValueType.TupleList" />: a whole number of groups.
/// </summary>
/// <remarks>
///     <para>
///         It used to demand a music event name and a positive weight, reading the type as "a
///         weighted list of music events". The type is the engine's generic pair list, and of its
///         three users one is a list of terrain and model pairs - which is how a vanilla
///         <c>Land_Terrain_Model_Mapping</c> came to be reported as a bad music event.
///     </para>
///     <para>
///         What each item IS belongs to the tag's slots, and is checked per item by the slot
///         handlers. This only counts.
///     </para>
/// </remarks>
public sealed class TupleListHandler : CommaSeparatedPairHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.TupleList;

    protected override XmlValueType TargetType => XmlValueType.TupleList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        return TupleShape.WholeGroups(fact) is { } error ? [error] : [];
    }
}

/// <summary>The shape checks the tuple handlers share, worded from the tag's slots.</summary>
internal static class TupleShape
{
    /// <summary>An error when the value is not a whole, non-zero number of groups.</summary>
    public static XmlDiagnosticResult? WholeGroups(XmlTagValueFact fact)
    {
        var count = TupleItems.Read(fact.Tag, fact.RawValue).Count;
        var size = TupleItems.GroupSize(fact.Tag);
        if (count > 0 && count % size == 0) return null;

        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            $"<{fact.Tag.Tag}> holds {count} item(s), which is not a whole number of {TupleItems.Describe(fact.Tag)}.");
    }
}