// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Parsing;

namespace PG.StarWarsGame.LSP.Lua.Tests.Parsing;

public sealed class LuaParseCacheTest
{
    private const string Uri = "file:///c:/scripts/mission.lua";
    private const string DiskPath = @"c:\scripts\mission.lua";

    private static (LuaParseCache Cache, GameWorkspaceHost Host) Build(
        MockFileSystem? fs = null, int capacity = 16)
    {
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var textSource = new DocumentTextSource(host, new FileHelper(fs ?? new MockFileSystem()),
            NullLogger<DocumentTextSource>.Instance);
        return (new LuaParseCache(textSource, capacity), host);
    }

    [Fact]
    public void GetOrParse_SameText_ReturnsSameInstance()
    {
        var (cache, _) = Build();

        var first = cache.GetOrParse(Uri, "function Foo() end");
        var second = cache.GetOrParse(Uri, "function Foo() end");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrParse_ChangedText_ReturnsNewParse()
    {
        var (cache, _) = Build();

        var first = cache.GetOrParse(Uri, "function Foo() end");
        var second = cache.GetOrParse(Uri, "function Bar() end");

        Assert.NotSame(first, second);
        Assert.Equal("function Bar() end", second.Text);
    }

    [Fact]
    public void GetOrParse_ParsesFunctionDeclarations()
    {
        var (cache, _) = Build();

        var parsed = cache.GetOrParse(Uri, "function Foo() end");

        Assert.Empty(parsed.Tree.GetDiagnostics());
        Assert.Contains("Foo", parsed.Tree.GetRoot().ToFullString());
    }

    [Fact]
    public void GetOrParse_ByUri_OpenDocument_ParsesBufferText()
    {
        var (cache, host) = Build();
        host.AddOrUpdate(Uri, "function FromBuffer() end", 1);

        var parsed = cache.GetOrParse(Uri);

        Assert.NotNull(parsed);
        Assert.Equal("function FromBuffer() end", parsed!.Text);
        Assert.Same(parsed, cache.GetOrParse(Uri));
    }

    [Fact]
    public void GetOrParse_ByUri_ClosedFile_ParsesDiskText()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new("function FromDisk() end") });
        var (cache, _) = Build(fs);

        var parsed = cache.GetOrParse(Uri);

        Assert.NotNull(parsed);
        Assert.Equal("function FromDisk() end", parsed!.Text);
    }

    [Fact]
    public void GetOrParse_ByUri_MissingEverywhere_ReturnsNull()
    {
        var (cache, _) = Build();

        Assert.Null(cache.GetOrParse(Uri));
    }

    [Fact]
    public void GetOrParse_ZeroCapacity_NeverCaches()
    {
        var (cache, _) = Build(capacity: 0);

        var first = cache.GetOrParse(Uri, "function Foo() end");
        var second = cache.GetOrParse(Uri, "function Foo() end");

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Statistics_ExposeUnderlyingCounters()
    {
        var (cache, _) = Build();

        _ = cache.GetOrParse(Uri, "function Foo() end"); // miss
        _ = cache.GetOrParse(Uri, "function Foo() end"); // hit

        var (hits, misses, _) = cache.Statistics;
        Assert.Equal(1, hits);
        Assert.Equal(1, misses);
    }
}
