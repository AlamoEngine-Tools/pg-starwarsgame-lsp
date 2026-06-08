// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Workspace;

public sealed class WorkspaceHostExtensionsTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    [Fact]
    public void TryGetOrReadFromDisk_HostHasDocument_ReturnsHostVersion()
    {
        var fs = new MockFileSystem();
        var fh = new FileHelper(fs);
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var uri = fh.PathToFileUri(Path.Combine(Root("ws"), "a.xml"));
        host.AddOrUpdate(uri, "<Editor/>", 3);

        var ok = host.TryGetOrReadFromDisk(fh, uri, out var doc);

        Assert.True(ok);
        Assert.Equal("<Editor/>", doc.Text);
        Assert.Equal(3, doc.Version);
    }

    [Fact]
    public void TryGetOrReadFromDisk_HostMiss_FileOnDisk_ReturnsDiskVersion()
    {
        var path = Path.Combine(Root("ws"), "a.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new("<Disk/>")
        });
        var fh = new FileHelper(fs);
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var uri = fh.PathToFileUri(path);

        var ok = host.TryGetOrReadFromDisk(fh, uri, out var doc);

        Assert.True(ok);
        Assert.Equal("<Disk/>", doc.Text);
        Assert.Equal(0, doc.Version);
        Assert.False(host.TryGet(uri, out _)); // disk read must NOT seed the host (avoids diagnostics flood)
    }

    [Fact]
    public void TryGetOrReadFromDisk_HostMiss_NoFile_ReturnsFalse()
    {
        var fs = new MockFileSystem();
        var fh = new FileHelper(fs);
        var host = new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
        var uri = fh.PathToFileUri(Path.Combine(Root("ws"), "missing.xml"));

        var ok = host.TryGetOrReadFromDisk(fh, uri, out _);

        Assert.False(ok);
    }
}