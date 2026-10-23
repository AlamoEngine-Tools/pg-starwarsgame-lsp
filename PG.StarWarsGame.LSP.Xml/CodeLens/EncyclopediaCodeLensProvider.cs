// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeLens;

/// <summary>
///     Emits a "preview encyclopedia" lens on GameObjects that have popup text, opening the preview
///     panel for that object.
/// </summary>
/// <remarks>
///     Deliberately not emitted on every object: a units file holds dozens, and a lens above each
///     one that opened an empty card would be noise on the objects that have nothing to show.
/// </remarks>
internal sealed class EncyclopediaCodeLensProvider : IXmlCodeLensProvider
{
    public const string ShowEncyclopediaCommand = "aet-eaw-edit.lsp.showEncyclopedia";

    private readonly ISchemaProvider _schema;
    private readonly IVariantTagSource _tagSource;

    public EncyclopediaCodeLensProvider(IVariantTagSource tagSource,
        ISchemaProvider schema)
    {
        _tagSource = tagSource;
        _schema = schema;
    }

    public LspCodeLens? Handle(CodeLensSymbolContext ctx)
    {
        // A planet's popup is a preview of its own, not the unit card.
        if (!EncyclopediaTags.HasCard(ctx.Index, _tagSource, ctx.Symbol, _schema.GetKind(ObjectKindNames.Planet)))
            return null;

        return new LspCodeLens
        {
            Range = new LspRange(new Position(ctx.Origin.Line, 0), new Position(ctx.Origin.Line, 0)),
            Command = new Command
            {
                Title = ctx.Messages.Format("EncyclopediaCodeLens_PreviewEncyclopedia"),
                Name = ShowEncyclopediaCommand,
                Arguments = JArray.FromObject(new object[] { ctx.Symbol.Id })
            }
        };
    }
}