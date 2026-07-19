// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Util;

public sealed class ContentHasherTest
{
    [Fact]
    public void Hash_SameText_IsDeterministic()
    {
        Assert.Equal(ContentHasher.Hash("<Root/>"), ContentHasher.Hash("<Root/>"));
    }

    [Fact]
    public void Hash_DifferentText_Differs()
    {
        Assert.NotEqual(ContentHasher.Hash("<Root/>"), ContentHasher.Hash("<Root2/>"));
    }

    [Fact]
    public void Hash_EmptyText_IsFnvOffsetBasis()
    {
        // FNV-1a 64-bit offset basis — pins the algorithm so the index, the text source, and the
        // parse cache can never silently disagree on the hash function.
        Assert.Equal(unchecked((long)14695981039346656037), ContentHasher.Hash(string.Empty));
    }

    [Fact]
    public async Task Hash_MatchesContentHashStampedByGameIndexService()
    {
        // The unchanged-content fast path in GameIndexService compares its stamped hash against
        // hashes computed elsewhere — both sides must use ContentHasher.
        const string text = "<GameObjectFiles><Unit Name=\"X\"/></GameObjectFiles>";
        var svc = new GameIndexService(new FileHelper(new MockFileSystem()), [new StubParser()],
            NullLogger<GameIndexService>.Instance);

        await svc.UpdateDocumentAsync("file:///f.xml", text, 1, default);

        Assert.Equal(ContentHasher.Hash(text), svc.Current.Documents["file:///f.xml"].ContentHash);
    }

    private sealed class StubParser : IGameDocumentParser
    {
        public bool CanParse(string ext)
        {
            return ext == ".xml";
        }

        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version, CancellationToken ct)
        {
            return ValueTask.FromResult(new DocumentIndex(uri, version,
                ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty));
        }
    }
}