// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Core.Tests.Testing;

/// <summary>
///     The open-document store every suite uses: keyed on the normalised URI, so the spelling a
///     handler asks with need not be the spelling the document was opened with.
/// </summary>
public sealed class FakeGameWorkspaceHostTest
{
    [Fact]
    public void AddOrUpdate_And_TryGet_RoundTripOnTwoSpellingsOfOneUri()
    {
        var host = new FakeGameWorkspaceHost();
        host.AddOrUpdate("file:///c%3A/Mods/a.xml", "<a/>", 3);

        Assert.True(host.TryGet(@"C:\Mods\A.XML", out var doc));
        Assert.Equal("<a/>", doc.Text);
        Assert.Equal(3, doc.Version);
        Assert.Single(host.All);
    }

    [Fact]
    public void AddOrUpdate_ReplacesTheDocument()
    {
        var host = new FakeGameWorkspaceHost();
        host.AddOrUpdate("file:///C:/a.xml", "<a/>", 1);
        host.AddOrUpdate("file:///c:/a.xml", "<b/>", 2, false);

        var doc = Assert.Single(host.All);
        Assert.Equal("<b/>", doc.Text);
        Assert.Equal(2, doc.Version);
        Assert.False(doc.PublishDiagnostics);
    }

    [Fact]
    public void Remove_ForgetsTheDocument()
    {
        var host = new FakeGameWorkspaceHost().With("file:///C:/a.xml", "<a/>");

        host.Remove("file:///c:/a.xml");

        Assert.False(host.TryGet("file:///C:/a.xml", out _));
        Assert.Empty(host.All);
    }

    [Fact]
    public void Writes_And_Removals_RecordEveryCallAsWritten()
    {
        var host = new FakeGameWorkspaceHost();

        host.AddOrUpdate("file:///C:/a.xml", "<a/>", 1);
        host.AddOrUpdate("file:///c:/a.xml", "<b/>", 2, false);
        host.Remove("file:///C:/A.xml");

        Assert.Equal(
            [
                new TrackedDocument("file:///C:/a.xml", "<a/>", 1),
                new TrackedDocument("file:///c:/a.xml", "<b/>", 2, false)
            ],
            host.Writes);
        Assert.Equal(["file:///C:/A.xml"], host.Removals);
        Assert.Empty(host.All);
    }

    [Fact]
    public void TryGet_OfAnUnknownUri_IsFalse()
    {
        Assert.False(new FakeGameWorkspaceHost().TryGet("file:///C:/missing.xml", out _));
    }

    [Fact]
    public void With_SeedsADocumentAtVersionOne_AndKeepsTheUriAsWritten()
    {
        var host = new FakeGameWorkspaceHost().With("file:///C:/a.xml", "<a/>");

        Assert.True(host.TryGet("file:///C:/a.xml", out var doc));
        Assert.Equal("file:///C:/a.xml", doc.Uri);
        Assert.Equal(1, doc.Version);
        Assert.True(doc.PublishDiagnostics);
    }
}