// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Rename;

public sealed class GroupValueRenameBuilderTest
{
    private const string DocA = "file:///mods/m/data/xml/campaigns_a.xml";
    private const string DocB = "file:///mods/m/data/xml/campaigns_b.xml";

    // (uri, groupKey, tagLine, tagColumn, tagLength) tuples become one DocumentGroupMembership each,
    // grouped by uri into per-document DocumentIndex entries. The parent Campaign origin is irrelevant
    // to the value rename (which rewrites the tag span), so it is left at column 0.
    private static GameIndex IndexWith(params (string Uri, string Key, int Line, int Col, int Len)[] members)
    {
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty;
        foreach (var group in members.GroupBy(m => m.Uri))
        {
            var dgms = group
                .Select(m => new DocumentGroupMembership(
                    new GroupMembership(m.Key, "Campaign", new FileOrigin(m.Uri, m.Line, 0)),
                    m.Line, m.Col, m.Len))
                .ToImmutableArray();
            docs = docs.Add(group.Key, new DocumentIndex(group.Key, 1,
                ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty,
                GroupMemberships: dgms));
        }

        return GameIndex.Empty with { Documents = docs };
    }

    private static IReadOnlyList<(string Uri, TextEdit Edit)> Edits(WorkspaceEdit edit)
    {
        return edit.DocumentChanges!
            .Where(c => c.IsTextDocumentEdit)
            .SelectMany(c => c.TextDocumentEdit!.Edits
                .Select(e => (c.TextDocumentEdit!.TextDocument.Uri.ToString(), e)))
            .ToList();
    }

    [Fact]
    public void Build_RewritesGroupValue_AcrossAllMemberDocuments()
    {
        var index = IndexWith(
            (DocA, "Story_Test_Set", 5, 20, "Story_Test_Set".Length),
            (DocB, "Story_Test_Set", 9, 12, "Story_Test_Set".Length));

        var result = GroupValueRenameBuilder.Build("Story_Test_Set", "Story_Test_Set2",
            index, NullLogger.Instance);

        Assert.NotNull(result);
        var edits = Edits(result!);
        Assert.Equal(2, edits.Count);
        Assert.All(edits, e => Assert.Equal("Story_Test_Set2", e.Edit.NewText));
        Assert.Contains(edits, e => e.Uri == DocA && e.Edit.Range.Start.Line == 5 &&
                                    e.Edit.Range.Start.Character == 20 &&
                                    e.Edit.Range.End.Character == 20 + "Story_Test_Set".Length);
        Assert.Contains(edits, e => e.Uri == DocB && e.Edit.Range.Start.Line == 9 &&
                                    e.Edit.Range.Start.Character == 12);
    }

    [Fact]
    public void Build_MultipleMembersInSameDocument_AllRewritten()
    {
        var index = IndexWith(
            (DocA, "MP_Set", 3, 15, "MP_Set".Length),
            (DocA, "MP_Set", 40, 15, "MP_Set".Length));

        var result = GroupValueRenameBuilder.Build("MP_Set", "MP_Set_New", index, NullLogger.Instance);

        var edits = Edits(result!);
        Assert.Equal(2, edits.Count);
        Assert.All(edits, e => Assert.Equal(DocA, e.Uri));
    }

    [Fact]
    public void Build_OnlyRewritesTheMatchingKey_NotOtherGroups()
    {
        var index = IndexWith(
            (DocA, "SetA", 1, 15, "SetA".Length),
            (DocA, "SetB", 2, 15, "SetB".Length));

        var result = GroupValueRenameBuilder.Build("SetA", "SetA2", index, NullLogger.Instance);

        var (_, edit) = Assert.Single(Edits(result!));
        Assert.Equal("SetA2", edit.NewText);
        Assert.Equal(1, edit.Range.Start.Line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Foo<Bar")]
    [InlineData("Foo&Bar")]
    [InlineData("Foo>Bar")]
    public void Build_InvalidNewValue_ReturnsNull(string newValue)
    {
        var index = IndexWith((DocA, "SetA", 1, 15, "SetA".Length));
        Assert.Null(GroupValueRenameBuilder.Build("SetA", newValue, index, NullLogger.Instance));
    }

    [Fact]
    public void Build_UnknownKey_ReturnsNull()
    {
        var index = IndexWith((DocA, "SetA", 1, 15, "SetA".Length));
        Assert.Null(GroupValueRenameBuilder.Build("Nope", "X", index, NullLogger.Instance));
    }

    [Fact]
    public void Build_RenameToSameValue_ReturnsNull()
    {
        var index = IndexWith((DocA, "SetA", 1, 15, "SetA".Length));
        Assert.Null(GroupValueRenameBuilder.Build("SetA", "SetA", index, NullLogger.Instance));
    }
}
