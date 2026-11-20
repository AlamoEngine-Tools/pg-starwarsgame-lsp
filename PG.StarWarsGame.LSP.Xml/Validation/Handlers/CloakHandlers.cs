// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a cloaking unit whose model has no part the cloak hides.
/// </summary>
/// <remarks>
///     A warning only when the cloak's look changes nothing either, since the unit then looks the
///     same cloaked; information otherwise, because a unit that only fades or tints may be meant.
/// </remarks>
public sealed class CloakHidesNothingHandler : XmlDiagnosticsHandler<CloakHidesNothingFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.CloakHidesNothing;

    protected override IEnumerable<XmlDiagnosticResult> Handle(CloakHidesNothingFact fact, DiagnosticsContext ctx)
    {
        return
        [
            fact.LookChangesNothing
                ? new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    ctx.Messages.Format("CloakHidesNothing_HidesNoPartLookUnchanged", fact.ModelName, fact.Ability))
                : new XmlDiagnosticResult(XmlDiagnosticSeverity.Information,
                    ctx.Messages.Format("CloakHidesNothing_HidesNoPart", fact.ModelName, fact.Ability))
        ];
    }
}

/// <summary>
///     Reports a cloaking unit whose model has no part the cloak shows.
/// </summary>
/// <remarks>Severity as for <see cref="CloakHidesNothingHandler" />.</remarks>
public sealed class CloakShowsNoShellHandler : XmlDiagnosticsHandler<CloakShowsNoShellFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.CloakShowsNoShell;

    protected override IEnumerable<XmlDiagnosticResult> Handle(CloakShowsNoShellFact fact, DiagnosticsContext ctx)
    {
        return
        [
            fact.LookChangesNothing
                ? new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    ctx.Messages.Format("CloakShowsNoShell_ShowsNoPartLookUnchanged", fact.ModelName, fact.Ability))
                : new XmlDiagnosticResult(XmlDiagnosticSeverity.Information,
                    ctx.Messages.Format("CloakShowsNoShell_ShowsNoPart", fact.ModelName, fact.Ability))
        ];
    }
}

/// <summary>
///     Reports a hardpoint's attached model that a Force cloak leaves drawn. Information: a state of
///     the unit, named, with nothing implied about whether it is meant.
/// </summary>
public sealed class ForceCloakKeepsAttachedModelHandler : XmlDiagnosticsHandler<ForceCloakKeepsAttachedModelFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ForceCloakKeepsAttachedModel;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        ForceCloakKeepsAttachedModelFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Information,
                ctx.Messages.Format("ForceCloakKeepsAttachedModel_StaysDrawn", fact.HardpointId, fact.AttachedModel))
        ];
    }
}

/// <summary>
///     Reports the parts a cloak leaves drawn as they were. Information: a state of the unit, named,
///     with nothing implied about whether it is meant.
/// </summary>
public sealed class CloakKeepsPartsHandler : XmlDiagnosticsHandler<CloakKeepsPartsFact>
{
    // Enough to recognise the model by; a hull of dozens of parts would otherwise fill the hover.
    private const int NamedParts = 8;

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.CloakKeepsParts;

    protected override IEnumerable<XmlDiagnosticResult> Handle(CloakKeepsPartsFact fact, DiagnosticsContext ctx)
    {
        var named = string.Join(", ", fact.Parts.Take(NamedParts));
        var parts = fact.Parts.Count > NamedParts
            ? ctx.Messages.Format("CloakKeepsParts_AndMore", named, fact.Parts.Count - NamedParts)
            : named;

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Information,
                ctx.Messages.Format("CloakKeepsParts_StayDrawn", fact.ModelName, fact.Ability, parts))
        ];
    }
}