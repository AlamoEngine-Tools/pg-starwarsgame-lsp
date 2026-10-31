// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A name defined more than once. Where the load order places every definition in one registry,
///     the game's rule decides, and only the definition it drops is told which one it keeps - or, for
///     the targeting sets, that it is merged. Where nothing orders them, each is told of the others.
/// </summary>
public sealed class DuplicateDefinitionHandler : XmlDiagnosticsHandler<XmlSymbolFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.DuplicateDefinition;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlSymbolFact fact, DiagnosticsContext ctx)
    {
        // Engine placeholders are redefined across files on purpose (Default SFXEvent, Default
        // TradeRouteLine), so their duplicates are only noted.
        var placeholder = EnginePlaceholders.IsPlaceholder(fact.SymbolId);

        if (fact is { Kept: { } kept, Rule: { } rule })
        {
            var keptAt = FileNameOf(kept);
            var (id, key, severity) = rule switch
            {
                DuplicateNameRule.MERGE => (DiagnosticIds.DefinitionMerged, "DefinitionMerged_IntoEarlier",
                    XmlDiagnosticSeverity.Information),
                DuplicateNameRule.LAST_WINS => (DiagnosticIds.DefinitionNotUsed, "DefinitionNotUsed_LastWins",
                    XmlDiagnosticSeverity.Warning),
                DuplicateNameRule.FIRST_BY_NAME => (DiagnosticIds.DefinitionNotUsed, "DefinitionNotUsed_FirstByName",
                    XmlDiagnosticSeverity.Warning),
                _ => (DiagnosticIds.DefinitionNotUsed, "DefinitionNotUsed_FirstWins", XmlDiagnosticSeverity.Warning)
            };
            var keptRelated = kept.Origin is FileOrigin { IsNavigable: true } keptOrigin
                ? new List<XmlRelatedLocation>
                {
                    new(keptOrigin.Uri, keptOrigin.Line, keptOrigin.Column,
                        ctx.Messages.Format("DuplicateDefinition_KeptDefinition", fact.SymbolId))
                }
                : null;

            yield return new XmlDiagnosticResult(placeholder ? XmlDiagnosticSeverity.Information : severity,
                ctx.Messages.Format(key, fact.SymbolId, fact.Registry ?? string.Empty, keptAt),
                RelatedLocations: keptRelated, Id: id);
            yield break;
        }

        var others = fact.AllDefinitions
            .Where(s => s.Origin is FileOrigin fo &&
                        !(string.Equals(fo.Uri, fact.DocumentUri, StringComparison.OrdinalIgnoreCase) &&
                          fo.Line == fact.Line))
            .ToList();
        if (others.Count == 0)
            yield break;

        // Each editor-openable other definition rides along as a clickable related location;
        // baseline (game-relative) origins stay message-only.
        var related = others
            .Select(s => (FileOrigin)s.Origin)
            .Where(fo => fo.IsNavigable)
            .Select(fo => new XmlRelatedLocation(fo.Uri, fo.Line, fo.Column,
                ctx.Messages.Format("DuplicateDefinition_OtherDefinition", fact.SymbolId)))
            .ToList();

        yield return new XmlDiagnosticResult(
            placeholder ? XmlDiagnosticSeverity.Information : XmlDiagnosticSeverity.Warning,
            ctx.Messages.Format("DuplicateDefinition_AlsoDefinedIn", fact.SymbolId,
                string.Join(", ", others.Select(s => ((FileOrigin)s.Origin).Uri))),
            RelatedLocations: related.Count > 0 ? related : null, Id: DiagnosticIds.DuplicateDefinition);
    }

    // The kept definition's file as a reader knows it: the file name, of a project file or a shipped one.
    private static string FileNameOf(GameSymbol symbol)
    {
        if (symbol.Origin is not FileOrigin origin)
            return symbol.Origin.ToString();
        var path = origin.Uri.Replace('\\', '/');
        return Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
    }
}