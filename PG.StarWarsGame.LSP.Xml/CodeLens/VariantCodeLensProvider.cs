// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Core.Symbols;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeLens;

/// <summary>
///     Emits a code lens on variant objects ("▲ variant of BASE — show effective object", which triggers the
///     effective-object view) and on base objects ("N variants").
/// </summary>
internal sealed class VariantCodeLensProvider : IXmlCodeLensProvider
{
    public const string ShowEffectiveCommand = "aet-eaw-edit.lsp.showEffectiveObject";

    public LspCodeLens? Handle(CodeLensSymbolContext ctx)
    {
        var range = new LspRange(new Position(ctx.Origin.Line, 0), new Position(ctx.Origin.Line, 0));

        if (!string.IsNullOrEmpty(ctx.Symbol.VariantBaseId))
            return new LspCodeLens
            {
                Range = range,
                Command = new Command
                {
                    Title = $"▲ variant of {ctx.Symbol.VariantBaseId} — show effective object",
                    Name = ShowEffectiveCommand,
                    Arguments = JArray.FromObject(new object[] { ctx.Symbol.Id })
                }
            };

        var variants = CollectVariants(ctx.Index, ctx.Symbol.Id);
        if (variants.Count == 0) return null;

        var title = variants.Count == 1
            ? "1 variant"
            : $"{variants.Count.ToString(CultureInfo.InvariantCulture)} variants";

        // Open the references peek showing just the child variant objects (their definition sites).
        var locations = variants.Select(o => new
        {
            uri = o.Uri,
            range = new
            {
                start = new { line = o.Line, character = o.Column ?? 0 },
                end = new { line = o.Line, character = o.Column ?? 0 }
            }
        });

        return new LspCodeLens
        {
            Range = range,
            Command = new Command
            {
                Title = title,
                Name = "aet-eaw-edit.lsp.showReferences",
                Arguments = JArray.FromObject(new object[]
                {
                    ctx.Origin.Uri,
                    new { line = ctx.Origin.Line, character = ctx.Origin.Column ?? 0 },
                    locations.ToArray()
                })
            }
        };
    }

    private static IReadOnlyList<FileOrigin> CollectVariants(GameIndex index, string baseId)
    {
        var origins = new List<FileOrigin>();
        foreach (var defs in index.WorkspaceDefinitions.Values)
        foreach (var sym in defs)
            if (string.Equals(sym.VariantBaseId, baseId, StringComparison.OrdinalIgnoreCase)
                && sym.Origin is FileOrigin fo)
                origins.Add(fo);
        return origins;
    }
}
