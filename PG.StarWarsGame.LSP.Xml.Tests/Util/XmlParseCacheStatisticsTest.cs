// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Util;

/// <summary>The parse cache describes itself for the server status: entries, hits, misses, evictions.</summary>
public sealed class XmlParseCacheStatisticsTest
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
        var cache = new XmlParseCache(new NoText(), 16);
        cache.GetOrParse("file:///a.xml", "<A/>");
        cache.GetOrParse("file:///a.xml", "<A/>");
        cache.GetOrParse("file:///b.xml", "<B/>");

        var stats = ((ICacheStatisticsSource)cache).Snapshot();

        Assert.Equal("xml-parse", stats.Name);
        Assert.Equal(2, stats.Entries);
        Assert.Equal((1L, 2L, 0L), (stats.Hits, stats.Misses, stats.Evictions));
    }
}
