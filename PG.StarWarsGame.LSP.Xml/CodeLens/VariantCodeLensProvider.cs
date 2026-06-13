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

        var count = CountVariants(ctx.Index, ctx.Symbol.Id);
        if (count == 0) return null;

        var title = count == 1 ? "1 variant" : $"{count.ToString(CultureInfo.InvariantCulture)} variants";
        return new LspCodeLens { Range = range, Command = new Command { Title = title } };
    }

    private static int CountVariants(GameIndex index, string baseId)
    {
        var count = 0;
        foreach (var defs in index.WorkspaceDefinitions.Values)
        foreach (var sym in defs)
            if (string.Equals(sym.VariantBaseId, baseId, StringComparison.OrdinalIgnoreCase))
                count++;
        return count;
    }
}
