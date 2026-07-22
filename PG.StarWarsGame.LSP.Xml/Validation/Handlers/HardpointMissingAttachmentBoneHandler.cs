// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a destroyable hardpoint with no <c>Attachment_Bone</c>. Error, not warning: the
///     hardpoint contradicts its own <c>Is_Destroyable</c>, the consequence is a gameplay bug rather
///     than a cosmetic one, and unlike the bone-exists-on-model checks it needs no model data, so it
///     is never a false positive from incomplete .alo extraction.
/// </summary>
public sealed class HardpointMissingAttachmentBoneHandler
    : XmlDiagnosticsHandler<HardpointMissingAttachmentBoneFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointMissingAttachmentBoneFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"Hardpoint '{fact.HardpointId}' is Is_Destroyable but has no <Attachment_Bone>. The engine "
                + "cannot attach it to the parent model, so it is indestructible and can never be shot off.")
        ];
    }
}
