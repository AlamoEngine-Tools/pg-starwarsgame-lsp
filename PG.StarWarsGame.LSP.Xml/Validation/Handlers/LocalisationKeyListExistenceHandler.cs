// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class LocalisationKeyListExistenceHandler : LocalisationKeyHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.NameReferenceList;

    protected override IEnumerable<XmlDiagnosticResult> HandleLocalisationKey(
        XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        return ListValueConstants.PrepareValueForSplit(fact.RawValue)
            .Split(ListValueConstants.GetListSeparators(), StringSplitOptions.RemoveEmptyEntries)
            .Select(key => CheckKey(key, ctx))
            .OfType<XmlDiagnosticResult>();
    }
}