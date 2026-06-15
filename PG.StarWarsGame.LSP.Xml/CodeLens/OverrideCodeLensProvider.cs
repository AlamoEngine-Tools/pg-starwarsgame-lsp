// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeLens;

/// <summary>
///     Emits a lens on a workspace object that overrides a same-id object from a lower project layer
///     (a dependency): "▽ overrides 'X' from &lt;project&gt;". Clicking opens the references peek at the
///     shadowed (overridden) definition. Only the winning (highest-layer) definition gets the lens;
///     baseline overrides are intentionally ignored (every mod redefines shipped objects).
/// </summary>
internal sealed class OverrideCodeLensProvider : IXmlCodeLensProvider
{
    public LspCodeLens? Handle(CodeLensSymbolContext ctx)
    {
        if (!ctx.Index.WorkspaceDefinitions.TryGetValue(ctx.Symbol.Id, out var all) || all.Length <= 1)
            return null;

        var myRank = ctx.Index.LayerRankOf(ctx.Symbol);
        if (myRank != all.Max(ctx.Index.LayerRankOf))
            return null; // not the winning definition

        var shadowed = all
            .Where(s => ctx.Index.LayerRankOf(s) < myRank)
            .OrderByDescending(ctx.Index.LayerRankOf)
            .FirstOrDefault();
        if (shadowed?.Origin is not FileOrigin shadowedOrigin)
            return null;

        var layerName = ctx.Index.Documents.TryGetValue(shadowedOrigin.Uri, out var shadowedDoc)
            ? shadowedDoc.LayerName
            : null;
        var from = string.IsNullOrEmpty(layerName) ? "a referenced project" : layerName;

        var range = new LspRange(new Position(ctx.Origin.Line, 0), new Position(ctx.Origin.Line, 0));
        var location = new
        {
            uri = shadowedOrigin.Uri,
            range = new
            {
                start = new { line = shadowedOrigin.Line, character = shadowedOrigin.Column ?? 0 },
                end = new { line = shadowedOrigin.Line, character = shadowedOrigin.Column ?? 0 }
            }
        };

        return new LspCodeLens
        {
            Range = range,
            Command = new Command
            {
                Title = $"▽ overrides '{ctx.Symbol.Id}' from {from}",
                Name = "aet-eaw-edit.lsp.showReferences",
                Arguments = JArray.FromObject(new object[]
                {
                    ctx.Origin.Uri,
                    new { line = ctx.Origin.Line, character = ctx.Origin.Column ?? 0 },
                    new[] { location }
                })
            }
        };
    }
}
