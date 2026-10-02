// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     An enum item of a slotted tuple, checked against its enum - case-insensitively, as every
///     enum value is.
/// </summary>
/// <remarks>
///     Its own id rather than the whole-tag enum check's: two handlers sharing an id could not be
///     suppressed apart.
/// </remarks>
public sealed class TupleSlotEnumHandler : XmlDiagnosticsHandler<XmlTupleSlotFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.TupleSlotEnumValue;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTupleSlotFact fact, DiagnosticsContext ctx)
    {
        return SlotItemChecks.Enum(fact.Tag, fact.Slot, fact.Value, ctx) is { } result ? [result] : [];
    }
}

/// <summary>
///     An asset item of a slotted tuple - model, texture, audio or map - checked by the rules a whole
///     tag of that kind gets, under ids of their own so the two can be suppressed apart. A model is
///     also checked to be an <c>.alo</c>.
/// </summary>
public sealed class TupleSlotAssetHandler : XmlDiagnosticsHandler<XmlTupleSlotFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.TupleSlotModelFileExistence;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTupleSlotFact fact, DiagnosticsContext ctx)
    {
        return SlotItemChecks.Asset(fact.Tag, fact.Slot, fact.Value, ctx) is { } result ? [result] : [];
    }
}