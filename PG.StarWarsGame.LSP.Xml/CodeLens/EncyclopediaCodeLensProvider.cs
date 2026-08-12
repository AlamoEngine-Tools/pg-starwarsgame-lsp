// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeLens;

/// <summary>
///     Emits a "preview encyclopedia" lens on GameObjects that have popup text, opening the preview
///     panel for that object. Gated on <c>features.tools.encyclopedia</c>.
/// </summary>
/// <remarks>
///     Deliberately not emitted on every object: a units file holds dozens, and a lens above each
///     one that opened an empty card would be noise on the objects that have nothing to show.
/// </remarks>
internal sealed class EncyclopediaCodeLensProvider : IXmlCodeLensProvider
{
    public const string ShowEncyclopediaCommand = "aet-eaw-edit.lsp.showEncyclopedia";

    private readonly ILspConfigurationProvider _config;
    private readonly IVariantTagSource _tagSource;

    public EncyclopediaCodeLensProvider(IVariantTagSource tagSource, ILspConfigurationProvider config)
    {
        _tagSource = tagSource;
        _config = config;
    }

    public LspCodeLens? Handle(CodeLensSymbolContext ctx)
    {
        if (!_config.Current.Features.Tools.Encyclopedia)
            return null;

        if (!EncyclopediaTags.DefinesBody(ctx.Index, _tagSource, ctx.Symbol))
            return null;

        return new LspCodeLens
        {
            Range = new LspRange(new Position(ctx.Origin.Line, 0), new Position(ctx.Origin.Line, 0)),
            Command = new Command
            {
                Title = "preview encyclopedia",
                Name = ShowEncyclopediaCommand,
                Arguments = JArray.FromObject(new object[] { ctx.Symbol.Id })
            }
        };
    }
}
