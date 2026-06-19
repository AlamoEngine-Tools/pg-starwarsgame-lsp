// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeLens;

internal sealed class ReferencesCodeLensProvider : IXmlCodeLensProvider
{
    public LspCodeLens? Handle(CodeLensSymbolContext ctx)
    {
        var count = ctx.Index.WorkspaceReferences.TryGetValue(ctx.Symbol.Id, out var refs) ? refs.Length : 0;
        var title = count == 1 ? "1 reference" : $"{count} references";
        var range = new LspRange(new Position(ctx.Origin.Line, 0), new Position(ctx.Origin.Line, 0));

        Command command;
        if (count > 0)
        {
            var locations = refs!.Select(r => new
            {
                uri = r.DocumentUri,
                range = new
                {
                    start = new { line = r.Line, character = r.Column },
                    end = new { line = r.Line, character = r.Column + r.Length }
                }
            });

            command = new Command
            {
                Title = title,
                Name = "aet-eaw-edit.lsp.showReferences",
                Arguments = JArray.FromObject(new object[]
                {
                    ctx.Origin.Uri,
                    new { line = ctx.Origin.Line, character = ctx.Origin.Column ?? 0 },
                    locations.ToArray()
                })
            };
        }
        else
        {
            command = new Command { Title = title };
        }

        return new LspCodeLens { Range = range, Command = command };
    }
}