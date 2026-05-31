// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaTransitiveRequireResolverTest
{
    private const string UriA = "file:///scripts/a.lua";
    private const string UriB = "file:///scripts/b.lua";
    private const string UriC = "file:///scripts/c.lua";
    private const string UriD = "file:///scripts/d.lua";
    private static readonly IFileHelper s_fileHelper = new FileHelper(new MockFileSystem());

    // Build a DocumentIndex whose RequireArgs are set to the provided raw args.
    private static DocumentIndex MakeDoc(string uri, params string[] requireArgs)
    {
        return new DocumentIndex(uri, 1, [], [], ImmutableArray.Create(requireArgs));
    }

    // Build the allDocuments dict from the supplied pairs.
    private static Dictionary<string, DocumentIndex> MakeDocs(
        params (string uri, DocumentIndex doc)[] entries)
    {
        var dict = new Dictionary<string, DocumentIndex>(StringComparer.OrdinalIgnoreCase);
        foreach (var (uri, doc) in entries)
            dict[uri] = doc;
        return dict;
    }

    private static IReadOnlySet<string> Resolve(
        IReadOnlySet<string> seeds,
        IReadOnlyDictionary<string, DocumentIndex> docs)
    {
        return LuaTransitiveRequireResolver.GetTransitiveDependencies(seeds, docs, s_fileHelper);
    }

    private static HashSet<string> Seeds(params string[] uris)
    {
        return new HashSet<string>(uris, StringComparer.OrdinalIgnoreCase);
    }

    // ── 1. empty seeds ────────────────────────────────────────────────────────

    [Fact]
    public void EmptySeeds_ReturnsEmptySet()
    {
        var result = Resolve(Seeds(), MakeDocs());
        Assert.Empty(result);
    }

    // ── 2. seed not in Documents ──────────────────────────────────────────────

    [Fact]
    public void SeedNotInDocuments_ReturnsSeedOnly()
    {
        var result = Resolve(Seeds(UriA), MakeDocs());
        Assert.Equal([UriA], result, StringComparer.OrdinalIgnoreCase);
    }

    // ── 3. linear chain A→B→C (seeds={B}) ────────────────────────────────────

    [Fact]
    public void LinearChain_IncludesAllTransitiveUris()
    {
        var docs = MakeDocs(
            (UriB, MakeDoc(UriB, "c")),
            (UriC, MakeDoc(UriC)));

        var result = Resolve(Seeds(UriB), docs);

        Assert.Contains(UriB, result);
        Assert.Contains(UriC, result);
        Assert.Equal(2, result.Count);
    }

    // ── 4. cycle: B→A and A→B ────────────────────────────────────────────────

    [Fact]
    public void Cycle_DoesNotInfiniteLoop_ReturnsBothUris()
    {
        var docs = MakeDocs(
            (UriA, MakeDoc(UriA, "b")),
            (UriB, MakeDoc(UriB, "a")));

        var result = Resolve(Seeds(UriB), docs);

        Assert.Contains(UriB, result);
        Assert.Contains(UriA, result);
        Assert.Equal(2, result.Count);
    }

    // ── 5. diamond: seeds={B,D}, B→C, D→C ────────────────────────────────────

    [Fact]
    public void Diamond_SharedTransitiveDep_AppearsOnce()
    {
        var docs = MakeDocs(
            (UriB, MakeDoc(UriB, "c")),
            (UriC, MakeDoc(UriC)),
            (UriD, MakeDoc(UriD, "c")));

        var result = Resolve(Seeds(UriB, UriD), docs);

        Assert.Contains(UriB, result);
        Assert.Contains(UriC, result);
        Assert.Contains(UriD, result);
        Assert.Equal(3, result.Count);
    }

    // ── 6. unresolvable arg in RequireArgs ───────────────────────────────────

    [Fact]
    public void UnresolvableRequireArg_IsSilentlySkipped()
    {
        var docs = MakeDocs(
            (UriB, MakeDoc(UriB, "nonexistent_module_xyz")));

        // "nonexistent_module_xyz.lua" is not in any workspace URI → resolve returns null
        var result = Resolve(Seeds(UriB), docs);

        Assert.Equal([UriB], result, StringComparer.OrdinalIgnoreCase);
    }

    // ── 7. relative require arg in RequireArgs ────────────────────────────────

    [Fact]
    public void RelativeRequireArg_IsSilentlySkipped()
    {
        var docs = MakeDocs(
            (UriB, MakeDoc(UriB, "./relative/path")));

        var result = Resolve(Seeds(UriB), docs);

        Assert.Equal([UriB], result, StringComparer.OrdinalIgnoreCase);
    }

    // ── 8. result set is case-insensitive ─────────────────────────────────────

    [Fact]
    public void ResultSet_IsCaseInsensitive()
    {
        var docs = MakeDocs(
            (UriB, MakeDoc(UriB)));

        var result = Resolve(Seeds(UriB), docs);

        // The URI was added with lowercase; Contains must still work with uppercase.
        Assert.Contains(UriB.ToUpperInvariant(), result);
    }
}