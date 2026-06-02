// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class Audio3dProviderNameHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.Audio3dProviderName;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.RawValue.Trim().Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'' is not a valid 3D audio provider name for <{fact.Tag.Tag}>.")
            ];

        return [];
    }
}