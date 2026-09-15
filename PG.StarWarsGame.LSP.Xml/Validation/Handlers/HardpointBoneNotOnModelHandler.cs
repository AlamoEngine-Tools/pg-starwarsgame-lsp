// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a hardpoint bone that is absent from a model attaching it. Warning rather than error:
///     the bone list comes from reading the .alo, and an incomplete read would otherwise turn into a
///     wall of false errors. The unambiguous case - a destroyable hardpoint with no attachment bone at
///     all - is an error and lives in <see cref="HardpointMissingAttachmentBoneHandler" />.
/// </summary>
public sealed class HardpointBoneNotOnModelHandler : XmlDiagnosticsHandler<HardpointBoneNotOnModelFact>
{
    /// <summary>The one tag that may name a mesh as well as a bone, and whose mismatch has a cost to state.</summary>
    private const string CollisionMeshTag = "Collision_Mesh";

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.HardpointBoneNotOnModel;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        HardpointBoneNotOnModelFact fact, DiagnosticsContext ctx)
    {
        var owner = string.Equals(fact.OwnerId, fact.HardpointId, StringComparison.OrdinalIgnoreCase)
            ? $"its own model '{fact.ModelName}'"
            : $"'{fact.ModelName}', the model of '{fact.OwnerId}' which attaches it";

        // Collision_Mesh is valid on either model, so the check has already looked at both by the
        // time it reports. Naming only one of them reads as though the other were still worth
        // opening, which is the first thing the author would go and do.
        var also = fact.AttachedModelName is null
            ? string.Empty
            : $", nor on '{fact.AttachedModelName}', the hardpoint's own Model_To_Attach";

        if (!string.Equals(fact.TagName, CollisionMeshTag, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"<{fact.TagName}> names bone '{fact.BoneName}', which does not exist on {owner}{also}.")
            ];
        }

        // Collision_Mesh says what the mismatch COSTS, because the obvious reading - nothing can hit
        // the hardpoint - is wrong. GameObjectClass::Take_Damage replaces the name it looks a hardpoint
        // up by with the hardpoint's OWN Collision_Mesh when a hit is aimed at it, and on land with the
        // nearest live targetable hardpoint's (Find_Closest_Hard_Point, SUB_GAME_MODE_LAND); the exact
        // _stricmp then matches that value against itself. So aimed fire and land projectiles land: the
        // Gargantuan's eight hardpoints all write such a name and still die. Only untargeted space fire
        // is looked up by the struck renderable's name, and that is always a mesh name
        // (alRenderableMesh::Get_Name, vtable slot 0x18) - a name on no model never comes back.
        //
        // "Does not exist" is kept on purpose: the hardpoint E2E smoke test keys on it.
        var message = $"<{fact.TagName}> names '{fact.BoneName}', which does not exist as a mesh or a bone "
                      + $"on {owner}{also}. Aimed fire and land projectiles still reach it; untargeted "
                      + "space fire never does.";

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, message,
                SuggestedFix: fact.SuggestedName,
                FixTitle: fact.SuggestedName is null ? null : $"Use the model's mesh '{fact.SuggestedName}'")
        ];
    }
}
