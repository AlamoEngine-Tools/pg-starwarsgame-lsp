// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Util;

public sealed class XmlParseCacheTest
{
    private const string Uri = "file:///c:/data/xml/units.xml";
    private const string DiskPath = @"c:\data\xml\units.xml";

    private static (XmlParseCache Cache, GameWorkspaceHost Host) Build(
        MockFileSystem? fs = null, int capacity = 16)
    {
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var textSource = new DocumentTextSource(host, new FileHelper(fs ?? new MockFileSystem()),
            NullLogger<DocumentTextSource>.Instance);
        return (new XmlParseCache(textSource, capacity), host);
    }

    // ── (uri, text) overload - caller already holds the text ────────────────

    [Fact]
    public void GetOrParse_SameText_ReturnsSameInstance()
    {
        var (cache, _) = Build();

        var first = cache.GetOrParse(Uri, "<Root/>");
        var second = cache.GetOrParse(Uri, "<Root/>");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrParse_ChangedText_ReturnsNewParse()
    {
        var (cache, _) = Build();

        var first = cache.GetOrParse(Uri, "<Root/>");
        var second = cache.GetOrParse(Uri, "<Root2/>");

        Assert.NotSame(first, second);
        Assert.Equal("<Root2/>", second.Text);
    }

    // ── (uri) overload - text resolved via IDocumentTextSource ──────────────

    [Fact]
    public void GetOrParse_ByUri_OpenDocument_ParsesBufferText()
    {
        var (cache, host) = Build();
        host.AddOrUpdate(Uri, "<Buffer/>", 1);

        var parsed = cache.GetOrParse(Uri);

        Assert.NotNull(parsed);
        Assert.Equal("<Buffer/>", parsed!.Text);
        Assert.Same(parsed, cache.GetOrParse(Uri));
    }

    [Fact]
    public void GetOrParse_ByUri_ClosedFile_ParsesDiskText()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new("<Disk/>") });
        var (cache, _) = Build(fs);

        var parsed = cache.GetOrParse(Uri);

        Assert.NotNull(parsed);
        Assert.Equal("<Disk/>", parsed!.Text);
    }

    [Fact]
    public void GetOrParse_ByUri_MissingEverywhere_ReturnsNull()
    {
        var (cache, _) = Build();

        Assert.Null(cache.GetOrParse(Uri));
    }

    [Fact]
    public void GetOrParse_ByUri_EditedBuffer_ReturnsFreshParse()
    {
        var (cache, host) = Build();
        host.AddOrUpdate(Uri, "<V1/>", 1);
        var v1 = cache.GetOrParse(Uri);

        host.AddOrUpdate(Uri, "<V2/>", 2);
        var v2 = cache.GetOrParse(Uri);

        Assert.NotSame(v1, v2);
        Assert.Equal("<V2/>", v2!.Text);
    }

    // ── configuration ────────────────────────────────────────────────────────

    [Fact]
    public void GetOrParse_ZeroCapacity_NeverCaches()
    {
        var (cache, _) = Build(capacity: 0);

        var first = cache.GetOrParse(Uri, "<Root/>");
        var second = cache.GetOrParse(Uri, "<Root/>");

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Statistics_ExposeUnderlyingCounters()
    {
        var (cache, _) = Build();

        _ = cache.GetOrParse(Uri, "<Root/>"); // miss
        _ = cache.GetOrParse(Uri, "<Root/>"); // hit

        var (hits, misses, _) = cache.Statistics;
        Assert.Equal(1, hits);
        Assert.Equal(1, misses);
    }

    // ── concurrent-reader safety of the shared HAP artifact ─────────────────
    // HtmlAgilityPack is not documented thread-safe; the HAP 1.12.4 source was verified
    // (2026-07-05): with an unmutated document, InnerHtml/OuterHtml are pure substring reads,
    // InnerText builds into a local StringBuilder, and the lazy writes (Name's _name/_optimizedName,
    // empty ChildNodes/Attributes collections) are idempotent computations from immutable inputs
    // published by atomic reference stores. This stress test pins that contract against future
    // HAP upgrades.

    [Fact]
    public async Task SharedParsedDocument_ConcurrentTraversal_YieldsConsistentResults()
    {
        var (cache, _) = Build();
        const string text = """
                            <GameObjectFiles>
                              <SpaceUnit Name="Fighter_A"><Max_Health>120</Max_Health><Mass>5</Mass></SpaceUnit>
                              <SpaceUnit Name="Fighter_B"><Damage>7</Damage></SpaceUnit>
                              <Empty/>
                            </GameObjectFiles>
                            """;
        var shared = cache.GetOrParse(Uri, text);
        var baseline = TraversalFingerprint(shared);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            var fingerprints = new List<string>();
            for (var i = 0; i < 50; i++)
                fingerprints.Add(TraversalFingerprint(shared));
            return fingerprints;
        })));

        Assert.All(results.SelectMany(r => r), fp => Assert.Equal(baseline, fp));
    }

    private static string TraversalFingerprint(ParsedXmlDocument parsed)
    {
        var names = new List<string>();
        var attrCount = 0;
        var innerTextLength = 0;
        var innerHtmlLength = 0;
        foreach (var node in parsed.Html.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            names.Add(node.Name);
            attrCount += node.Attributes.Count;
            innerTextLength += node.InnerText.Length;
            innerHtmlLength += node.InnerHtml.Length;
            _ = node.ChildNodes.Count; // touch lazy empty collections on leaf nodes
            _ = node.OuterHtml.Length;
            _ = parsed.LineIndex.GetPosition(node.StreamPosition);
        }

        return $"{string.Join(',', names)}|{attrCount}|{innerTextLength}|{innerHtmlLength}";
    }
}