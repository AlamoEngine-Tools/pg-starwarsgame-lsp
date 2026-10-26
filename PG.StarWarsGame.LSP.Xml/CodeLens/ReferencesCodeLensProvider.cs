// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeLens;

internal sealed class ReferencesCodeLensProvider : IXmlCodeLensProvider
{
    public LspCodeLens Handle(CodeLensSymbolContext ctx)
    {
        var refs = ctx.Index.WorkspaceReferences.TryGetValue(ctx.Symbol.Id, out var found) ? found : [];
        return For(ctx.Origin.Uri, ctx.Origin.Line, ctx.Origin.Column ?? 0, refs, ctx.Messages);
    }

    /// <summary>
    ///     A definition's references lens: their count, opening them where there are any. Shared by the
    ///     symbols and by the dynamic enum values, whose uses are references of their own.
    /// </summary>
    /// <param name="uri">The document the definition is in.</param>
    /// <param name="line">The definition's line; the lens sits at its start.</param>
    /// <param name="column">Where the definition starts on its line, for the references view.</param>
    /// <param name="refs">The references to the definition.</param>
    /// <param name="messages">The catalogue the lens text comes from.</param>
    public static LspCodeLens For(string uri, int line, int column, IReadOnlyList<GameReference> refs,
        IMessageCatalog messages)
    {
        var count = refs.Count;
        var title = count == 1
            ? messages.Format("ReferencesCodeLens_Reference")
            : messages.Format("ReferencesCodeLens_References", count);
        var range = new LspRange(new Position(line, 0), new Position(line, 0));

        Command command;
        if (count > 0)
        {
            var locations = refs.Select(r => new
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
                    uri,
                    new { line, character = column },
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