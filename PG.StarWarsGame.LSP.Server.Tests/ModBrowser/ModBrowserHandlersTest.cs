// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.ModBrowser;
using PG.StarWarsGame.LSP.Server.Tests.Preview;
using System.IO.Abstractions.TestingHelpers;

namespace PG.StarWarsGame.LSP.Server.Tests.ModBrowser;

/// <summary>The Mod Browser's requests, and their setting (#270).</summary>
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

    private static FakeLspConfigurationProvider Config(bool modBrowser)
    {
        return FakeLspConfigurationProvider.WithFeatures(new FeatureFlags
        {
            Tools = new ToolsFeatureFlags { ModBrowser = modBrowser }
        });
    }

    private sealed class NoTags : IVariantTagSource
    {
        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return null;
        }
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task TheSetting_GatesEveryRequest(bool modBrowser, int expected)
    {
        var index = new FakeGameIndexService(s_index);
        var schema = new NullSchemaProvider();
        var layers = new ProjectLayerMap(new FileHelper(new MockFileSystem()));
        var config = Config(modBrowser);
        var assets = new FakeAssets();

        var outline = await new GetSymbolOutlineHandler(index, schema, layers, config)
            .Handle(new GetSymbolOutlineParams(SymbolOutlines.GameData), CancellationToken.None);
        var page = await new GetSymbolPageHandler(index, schema, new NoTags(), assets, config)
            .Handle(new GetSymbolPageParams("xml:GameObjectType"), CancellationToken.None);
        var node = await new GetSymbolNodeHandler(index, schema, new NoTags(), assets, config)
            .Handle(new GetSymbolNodeParams("xml:GameObjectType", "Star_Destroyer"), CancellationToken.None);

        Assert.Equal(expected, outline.Pages.Count);
        Assert.Equal(expected, page.Total);
        Assert.Equal(expected == 1, node.Entry is not null);
    }
}
