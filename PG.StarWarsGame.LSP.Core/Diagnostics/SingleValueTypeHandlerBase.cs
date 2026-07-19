// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Base for handlers that validate a single <see cref="XmlValueType" />. Gates once on
///     <see cref="TargetType" /> - when the fact's value type differs, no diagnostics are
///     produced and <see cref="HandleValue" /> is never called. This is the single source of
///     truth for the value-type gate, replacing the per-handler
///     <c>if (fact.Tag.ValueType != X) return [];</c> guard.
/// </summary>
public abstract class SingleValueTypeHandlerBase : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected abstract XmlValueType TargetType { get; }

    public sealed override XmlValueType? HandledValueType => TargetType;

    protected sealed override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != TargetType)
            return [];

        return HandleValue(fact, ctx);
    }

    /// <summary>
    ///     Called only when <see cref="XmlTagValueFact.Tag" />'s value type equals
    ///     <see cref="TargetType" />. Implementations validate the value and emit diagnostics.
    /// </summary>
    protected abstract IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx);
}