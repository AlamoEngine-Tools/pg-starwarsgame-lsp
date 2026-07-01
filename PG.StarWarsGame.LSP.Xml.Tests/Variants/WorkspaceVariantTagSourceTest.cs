// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Variants;

namespace PG.StarWarsGame.LSP.Xml.Tests.Variants;

public sealed class WorkspaceVariantTagSourceTest
{
    [Fact]
    public void TryGetTags_ReturnsDirectChildTagsOfObject()
    {
        var host = new FakeHost();
        host.AddOrUpdate("file:///u.xml",
            """<GameObjectFiles><SpaceUnit Name="V"><Max_Health>100</Max_Health><Mass>5</Mass></SpaceUnit></GameObjectFiles>""",
            1);

        var tags = new WorkspaceVariantTagSource(host).TryGetTags("V");

        Assert.NotNull(tags);
        Assert.Equal(2, tags!.Count);
        Assert.Contains(tags, t => t.TagName == "Max_Health" && t.Value == "100");
        Assert.Contains(tags, t => t.TagName == "Mass" && t.Value == "5");
    }

    [Fact]
    public void TryGetTags_FragmentPreservesOriginalCaseAndText()
    {
        var host = new FakeHost();
        host.AddOrUpdate("file:///u.xml",
            """<GameObjectFiles><SpaceUnit Name="V"><Max_Health>100</Max_Health></SpaceUnit></GameObjectFiles>""", 1);

        var tag = Assert.Single(new WorkspaceVariantTagSource(host).TryGetTags("V")!);

        Assert.Equal("<Max_Health>100</Max_Health>", tag.Fragment);
    }

    [Fact]
    public void TryGetTags_OriginPointsToTagLineInDocument()
    {
        var host = new FakeHost();
        host.AddOrUpdate("file:///u.xml",
            "<GameObjectFiles>\n<SpaceUnit Name=\"V\">\n<Max_Health>100</Max_Health>\n</SpaceUnit>\n</GameObjectFiles>",
            1);

        var tag = Assert.Single(new WorkspaceVariantTagSource(host).TryGetTags("V")!);

        var origin = Assert.IsType<FileOrigin>(tag.Origin);
        Assert.Equal("file:///u.xml", origin.Uri);
        Assert.Equal(2, origin.Line); // 0-based line of <Max_Health>
    }

    [Fact]
    public void TryGetTags_UnknownObject_ReturnsNull()
    {
        var host = new FakeHost();
        host.AddOrUpdate("file:///u.xml", """<GameObjectFiles><SpaceUnit Name="V"/></GameObjectFiles>""", 1);

        Assert.Null(new WorkspaceVariantTagSource(host).TryGetTags("MISSING"));
    }

    [Fact]
    public void TryGetTags_CaseInsensitiveId()
    {
        var host = new FakeHost();
        host.AddOrUpdate("file:///u.xml", """<X><SpaceUnit Name="MyUnit"><Hp>1</Hp></SpaceUnit></X>""", 1);

        Assert.NotNull(new WorkspaceVariantTagSource(host).TryGetTags("MYUNIT"));
    }

    [Fact]
    public void TryGetTags_FindsObjectAcrossMultipleDocuments()
    {
        var host = new FakeHost();
        host.AddOrUpdate("file:///a.xml", """<X><SpaceUnit Name="A"><Hp>1</Hp></SpaceUnit></X>""", 1);
        host.AddOrUpdate("file:///b.xml", """<X><SpaceUnit Name="B"><Hp>2</Hp></SpaceUnit></X>""", 1);

        var tags = new WorkspaceVariantTagSource(host).TryGetTags("B");

        Assert.NotNull(tags);
        Assert.Contains(tags!, t => t.TagName == "Hp" && t.Value == "2");
    }

    private sealed class FakeHost : IGameWorkspaceHost
    {
        private readonly List<TrackedDocument> _docs = [];

        public IEnumerable<TrackedDocument> All => _docs;

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs.Add(new TrackedDocument(uri, text, version, publishDiagnostics));
        }

        public void Remove(string uri)
        {
            _docs.RemoveAll(d => d.Uri == uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            doc = _docs.FirstOrDefault(d => d.Uri == uri)!;
            return doc is not null;
        }
    }
}