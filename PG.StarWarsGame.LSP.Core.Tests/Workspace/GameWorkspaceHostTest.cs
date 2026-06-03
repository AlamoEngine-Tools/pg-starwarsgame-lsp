// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Workspace;

public sealed class GameWorkspaceHostTest
{
    private static IGameWorkspaceHost Build()
    {
        return new GameWorkspaceHost(NullLogger<GameWorkspaceHost>.Instance);
    }

    // ── AddOrUpdate ──────────────────────────────────────────────────────────

    [Fact]
    public void AddOrUpdate_Adds_New_Document()
    {
        var host = Build();
        host.AddOrUpdate("file:///a.xml", "<Root/>", 1);

        Assert.True(host.TryGet("file:///a.xml", out var doc));
        Assert.Equal("file:///a.xml", doc.Uri);
        Assert.Equal("<Root/>", doc.Text);
        Assert.Equal(1, doc.Version);
    }

    [Fact]
    public void AddOrUpdate_Replaces_Existing_Document()
    {
        var host = Build();
        host.AddOrUpdate("file:///a.xml", "<Old/>", 1);
        host.AddOrUpdate("file:///a.xml", "<New/>", 2);

        Assert.True(host.TryGet("file:///a.xml", out var doc));
        Assert.Equal("<New/>", doc.Text);
        Assert.Equal(2, doc.Version);
    }

    [Fact]
    public void AddOrUpdate_Stores_Multiple_Distinct_Uris()
    {
        var host = Build();
        host.AddOrUpdate("file:///a.xml", "<A/>", 1);
        host.AddOrUpdate("file:///b.xml", "<B/>", 1);

        Assert.Equal(2, host.All.Count());
    }

    // ── Remove ───────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_Deletes_Existing_Document()
    {
        var host = Build();
        host.AddOrUpdate("file:///a.xml", "<Root/>", 1);
        host.Remove("file:///a.xml");

        Assert.False(host.TryGet("file:///a.xml", out _));
    }

    [Fact]
    public void Remove_Is_Idempotent_For_Unknown_Uri()
    {
        var host = Build();
        host.Remove("file:///never-added.xml"); // must not throw
    }

    // ── TryGet ───────────────────────────────────────────────────────────────

    [Fact]
    public void TryGet_Returns_False_For_Unknown_Uri()
    {
        var host = Build();
        Assert.False(host.TryGet("file:///missing.xml", out _));
    }

    [Fact]
    public void TryGet_Returns_True_And_Document_For_Known_Uri()
    {
        var host = Build();
        host.AddOrUpdate("file:///a.xml", "<Root/>", 3);

        var found = host.TryGet("file:///a.xml", out var doc);

        Assert.True(found);
        Assert.NotNull(doc);
        Assert.Equal(3, doc.Version);
    }

    // ── All ──────────────────────────────────────────────────────────────────

    [Fact]
    public void All_Returns_Empty_When_No_Documents()
    {
        Assert.Empty(Build().All);
    }

    [Fact]
    public void All_Returns_All_Tracked_Documents()
    {
        var host = Build();
        host.AddOrUpdate("file:///a.xml", "<A/>", 1);
        host.AddOrUpdate("file:///b.xml", "<B/>", 1);

        var uris = host.All.Select(d => d.Uri).OrderBy(u => u).ToList();
        Assert.Equal(["file:///a.xml", "file:///b.xml"], uris);
    }

    [Fact]
    public void All_Does_Not_Include_Removed_Documents()
    {
        var host = Build();
        host.AddOrUpdate("file:///a.xml", "<A/>", 1);
        host.AddOrUpdate("file:///b.xml", "<B/>", 1);
        host.Remove("file:///a.xml");

        Assert.Single(host.All);
        Assert.Equal("file:///b.xml", host.All.Single().Uri);
    }

    // ── case-insensitive URI matching ─────────────────────────────────────────

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        // Scanner stores normalized lowercase; editor requests use mixed-case drive letters.
        var host = Build();
        host.AddOrUpdate("file:///d:/units.xml", "<Root/>", 1);

        Assert.True(host.TryGet("file:///D:/units.xml", out var doc));
        Assert.Equal("<Root/>", doc.Text);
    }

    [Fact]
    public void AddOrUpdate_WithCaseVariant_TreatsAsOneEntry()
    {
        var host = Build();
        host.AddOrUpdate("file:///d:/units.xml", "<Old/>", 1);
        host.AddOrUpdate("file:///D:/units.xml", "<New/>", 2);

        Assert.Equal(1, host.All.Count());
        Assert.True(host.TryGet("file:///d:/units.xml", out var doc));
        Assert.Equal("<New/>", doc.Text);
    }

    [Fact]
    public void Remove_WithCaseVariant_RemovesExistingEntry()
    {
        var host = Build();
        host.AddOrUpdate("file:///d:/units.xml", "<Root/>", 1);
        host.Remove("file:///D:/units.xml");

        Assert.Empty(host.All);
    }
}