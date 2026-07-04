// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Workspace;

public sealed class DocumentTextSourceTest
{
    private const string Uri = "file:///c:/data/xml/units.xml";
    private const string DiskPath = @"c:\data\xml\units.xml";

    private static (DocumentTextSource Source, GameWorkspaceHost Host) Build(MockFileSystem fs)
    {
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var source = new DocumentTextSource(host, new FileHelper(fs),
            NullLogger<DocumentTextSource>.Instance);
        return (source, host);
    }

    [Fact]
    public void GetText_OpenBufferWinsOverDisk()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new("<Disk/>") });
        var (source, host) = Build(fs);
        host.AddOrUpdate(Uri, "<Buffer/>", 3);

        var result = source.GetText(Uri);

        Assert.NotNull(result);
        Assert.Equal("<Buffer/>", result!.Text);
        Assert.True(result.FromOpenBuffer);
    }

    [Fact]
    public void GetText_ClosedFile_ReadsFromDisk()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new("<Disk/>") });
        var (source, _) = Build(fs);

        var result = source.GetText(Uri);

        Assert.NotNull(result);
        Assert.Equal("<Disk/>", result!.Text);
        Assert.False(result.FromOpenBuffer);
    }

    [Fact]
    public void GetText_MissingEverywhere_ReturnsNull()
    {
        var (source, _) = Build(new MockFileSystem());

        Assert.Null(source.GetText(Uri));
    }

    [Fact]
    public void GetText_NonFileUri_ReturnsNull()
    {
        var (source, _) = Build(new MockFileSystem());

        Assert.Null(source.GetText("untitled:Untitled-1"));
    }

    [Fact]
    public void GetText_ContentHashMatchesContentHasher()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new("<Disk/>") });
        var (source, _) = Build(fs);

        var result = source.GetText(Uri);

        Assert.Equal(ContentHasher.Hash("<Disk/>"), result!.ContentHash);
    }
}
