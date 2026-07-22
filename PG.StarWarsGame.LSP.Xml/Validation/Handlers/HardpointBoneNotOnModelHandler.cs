// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a hardpoint bone that is absent from a model mounting it. Warning rather than error:
///     the bone list comes from reading the .alo, and an incomplete read would otherwise turn into a
///     wall of false errors. The unambiguous case - a destroyable hardpoint with no attachment bone at
///     all - is an error and lives in <see cref="HardpointMissingAttachmentBoneHandler" />.
/// </summary>
public sealed class HardpointBoneNotOnModelHandler : XmlDiagnosticsHandler<HardpointBoneNotOnModelFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointBoneNotOnModelFact fact, DiagnosticsContext ctx)
    {
        var owner = string.Equals(fact.OwnerId, fact.HardpointId, StringComparison.OrdinalIgnoreCase)
            ? $"its own model '{fact.ModelName}'"
            : $"'{fact.ModelName}', the model of '{fact.OwnerId}' which mounts it";

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"<{fact.TagName}> names bone '{fact.BoneName}', which does not exist on {owner}.")
        ];
    }
}
