// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class TextureFileFormatHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != ReferenceKind.TextureFile)
            return [];

        var value = fact.RawValue;
        if (!value.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) &&
            !value.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{value}' is not a valid texture filename for <{fact.Tag.Tag}>. Expected a .tga or .dds file.")
            ];

        return [];
    }
}
