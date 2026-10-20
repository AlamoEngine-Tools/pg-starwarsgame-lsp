// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A game object whose name shares the engine's name hash with another object's - see
///     <see cref="ObjectNameCollisions" />.
/// </summary>
/// <remarks>
///     A Warning on EVERY side, naming the others. The engine keeps whichever it loads first, and the
///     load order is not measured, so telling one side it is the unreachable one would be a guess.
///     Vanilla has none - 22,425 named objects across eaw and foc, no two sharing a hash - so every
///     one of these is a mod's.
/// </remarks>
public sealed class NameCrcCollisionHandler : XmlDiagnosticsHandler<XmlNameCrcCollisionFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ObjectNameCrcCollision;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlNameCrcCollisionFact fact, DiagnosticsContext ctx)
    {
        if (fact.Others.Count == 0) return [];

        var names = string.Join(", ", fact.Others.Select(s => $"'{s.Id}'"));
        var related = fact.Others
            .Select(s => s.Origin)
            .OfType<FileOrigin>()
            .Where(fo => fo.IsNavigable)
            .Select(fo => new XmlRelatedLocation(fo.Uri, fo.Line, fo.Column,
                ctx.Messages.Format("NameCrcCollision_SameNameHash")))
            .ToList();

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                ctx.Messages.Format("NameCrcCollision_HasTheSameNameHash", fact.SymbolId, names, fact.Hash),
                RelatedLocations: related.Count > 0 ? related : null)
        ];
    }
}