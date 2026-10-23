// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.ModBrowser;
using PG.StarWarsGame.LSP.Server.Tests.Preview;
using System.IO.Abstractions.TestingHelpers;

namespace PG.StarWarsGame.LSP.Server.Tests.ModBrowser;

/// <summary>The Mod Browser's requests (#270).</summary>
public sealed class ModBrowserHandlersTest
{
    private static readonly GameIndex s_index = GameIndex.Empty with
    {
        Baseline = BaselineIndex.Empty with
        {
            Symbols = ImmutableDictionary<string, GameSymbol>.Empty.Add("Star_Destroyer",
                new GameSymbol("Star_Destroyer", GameSymbolKind.XmlObject, "GameObjectType",
                    new UnknownOrigin("baseline"), null))
        }
    };

    private sealed class NoTags : IVariantTagSource
    {
        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return null;
        }
    }

    [Fact]
    public async Task EveryRequest_AnswersFromTheIndex()
    {
        var index = new FakeGameIndexService(s_index);
        var schema = new NullSchemaProvider();
        var layers = new ProjectLayerMap(new FileHelper(new MockFileSystem()));
        var assets = new FakeAssets();

        var outline = await new GetSymbolOutlineHandler(index, schema, layers)
            .Handle(new GetSymbolOutlineParams(SymbolOutlines.GameData), CancellationToken.None);
        var page = await new GetSymbolPageHandler(index, schema, new NoTags(), assets)
            .Handle(new GetSymbolPageParams("xml:GameObjectType"), CancellationToken.None);
        var node = await new GetSymbolNodeHandler(index, schema, new NoTags(), assets,
                new FileHelper(new MockFileSystem()))
            .Handle(new GetSymbolNodeParams("xml:GameObjectType", "Star_Destroyer"), CancellationToken.None);

        Assert.Single(outline.Pages);
        Assert.Equal(1, page.Total);
        Assert.NotNull(node.Entry);
    }
}