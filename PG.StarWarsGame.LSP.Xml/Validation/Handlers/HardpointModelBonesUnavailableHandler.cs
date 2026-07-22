// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports that a model's bones could not be read, so the hardpoint bones pointing at it went
///     unchecked. Surfaced deliberately: staying quiet would be indistinguishable from "everything
///     checks out", and the usual causes - a missing, corrupt, or unsupported .alo - are worth knowing.
/// </summary>
public sealed class HardpointModelBonesUnavailableHandler
    : XmlDiagnosticsHandler<HardpointModelBonesUnavailableFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointModelBonesUnavailableFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"No bone data could be read for model '{fact.ModelName}' (declared by '{fact.OwnerId}'), "
                + "so this bone reference was not verified. The model may be missing, unreadable, or an "
                + "unsupported .alo version.")
        ];
    }
}
