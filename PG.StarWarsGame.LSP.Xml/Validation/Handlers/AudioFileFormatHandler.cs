// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class AudioFileFormatHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != ReferenceKind.AudioFile)
            return [];

        var results = new List<XmlDiagnosticResult>();
        var tokens = ListValueConstants.PrepareValueForSplit(fact.RawValue)
            .Split(ListValueConstants.GetListSeparators(), StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
            if (!token.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                !token.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{token}' is not a valid audio filename for <{fact.Tag.Tag}>. Expected a .wav or .mp3 file."));

        return results;
    }
}