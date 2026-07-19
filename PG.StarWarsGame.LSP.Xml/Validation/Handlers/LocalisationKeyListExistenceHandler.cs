// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class LocalisationKeyListExistenceHandler : LocalisationKeyHandlerBase
{
    // Encyclopedia_Text/MP_Encyclopedia_Text are declared as TypeReferenceList in schema even
    // though they're semantically a localisation-key list like any NameReferenceList tag - the
    // inlay-hint provider already treats both as equivalent (LocalisationKeyMultiValueInlayHintProvider).
    protected override IReadOnlyList<XmlValueType> TargetTypes =>
        [XmlValueType.NameReferenceList, XmlValueType.TypeReferenceList];

    protected override IEnumerable<XmlDiagnosticResult> HandleLocalisationKey(
        XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        // SplitListWithOffsets preserves each token's offset within the original RawValue (unlike
        // PrepareValueForSplit's whitespace-collapsing), so a missing key inside a multi-line list
        // is highlighted at its own line, not the whole list value's start.
        return XmlUtility.SplitListWithOffsets(fact.RawValue)
            .Select(t => CheckKey(t.Token, ctx,
                XmlUtility.AdvancePosition(fact.Line, fact.Column, fact.RawValue, t.Offset)))
            .OfType<XmlDiagnosticResult>();
    }
}