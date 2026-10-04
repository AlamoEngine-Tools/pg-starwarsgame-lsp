// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Parsing;

namespace PG.StarWarsGame.LSP.Lua.Tests.Parsing;

/// <summary>The Lua parse cache describes itself for the server status.</summary>
public sealed class LuaParseCacheStatisticsTest
{
    private sealed class NoText : IDocumentTextSource
    {
        public DocumentText? GetText(string canonicalUri)
        {
            return null;
        }
    }

    [Fact]
    public void Snapshot_ReportsEntriesAndCountersUnderAStableName()
    {
        var cache = new LuaParseCache(new NoText(), 16);
        cache.GetOrParse("file:///a.lua", "local a = 1");
        cache.GetOrParse("file:///a.lua", "local a = 1");

        var stats = ((ICacheStatisticsSource)cache).Snapshot();

        Assert.Equal("lua-parse", stats.Name);
        Assert.Equal(1, stats.Entries);
        Assert.Equal((1L, 1L, 0L), (stats.Hits, stats.Misses, stats.Evictions));
    }
}
