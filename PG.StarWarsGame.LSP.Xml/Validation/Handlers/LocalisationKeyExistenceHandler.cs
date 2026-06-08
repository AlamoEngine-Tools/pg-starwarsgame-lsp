// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class LocalisationKeyExistenceHandler : LocalisationKeyHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.NameReference;

    protected override IEnumerable<XmlDiagnosticResult> HandleLocalisationKey(
        XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var key = fact.RawValue.Trim();
        if (string.IsNullOrEmpty(key))
            return [];

        var result = CheckKey(key, ctx);
        return result is null ? [] : [result];
    }
}