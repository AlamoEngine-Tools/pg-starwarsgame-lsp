// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaFileClassifierTest
{
    private const string LibraryUri = "file:///scripts/library/pgstatemachine.lua";
    private const string LibraryUri2 = "file:///scripts/library/pgevents.lua";
    private const string DependencyUri = "file:///scripts/heroplansonly.lua";
    private const string StandaloneA = "file:///scripts/ai/plan_a.lua";
    private const string StandaloneB = "file:///scripts/ai/plan_b.lua";
    private static readonly IFileHelper s_fileHelper = new FileHelper(new MockFileSystem());

    private static DocumentIndex MakeDoc(string uri, params string[] requireArgs)
    {
        return new DocumentIndex(uri, 1, [], [], ImmutableArray.Create(requireArgs));
    }

    private static Dictionary<string, DocumentIndex> MakeDocs(
        params (string uri, DocumentIndex doc)[] entries)
    {
        var dict = new Dictionary<string, DocumentIndex>(StringComparer.OrdinalIgnoreCase);
        foreach (var (uri, doc) in entries)
            dict[uri] = doc;
        return dict;
    }

    // ── GetTier ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetTier_UriUnderLibraryDirectory_ReturnsLibrary()
    {
        var docs = MakeDocs((LibraryUri, MakeDoc(LibraryUri)));
        var tier = LuaFileClassifier.GetTier(LibraryUri, docs, s_fileHelper);
        Assert.Equal(LuaFileTier.Library, tier);
    }

    [Fact]
    public void GetTier_LibraryUri_IsLibraryEvenIfNobodyRequiresIt()
    {
        // LibraryUri has no incoming requires from other docs, yet it's still Library by path
        var docs = MakeDocs(
            (LibraryUri, MakeDoc(LibraryUri)),
            (StandaloneA, MakeDoc(StandaloneA)));
        var tier = LuaFileClassifier.GetTier(LibraryUri, docs, s_fileHelper);
        Assert.Equal(LuaFileTier.Library, tier);
    }

    [Fact]
    public void GetTier_LibraryUri_IsLibraryEvenIfOthersAlsoRequireIt()
    {
        // Library path takes precedence over "required by others" → still Library
        var docs = MakeDocs(
            (LibraryUri, MakeDoc(LibraryUri)),
            (StandaloneA, MakeDoc(StandaloneA, "pgstatemachine")));
        var tier = LuaFileClassifier.GetTier(LibraryUri, docs, s_fileHelper);
        Assert.Equal(LuaFileTier.Library, tier);
    }

    [Fact]
    public void GetTier_RequiredByOtherFile_NotInLibraryDirectory_ReturnsDependency()
    {
        var docs = MakeDocs(
            (DependencyUri, MakeDoc(DependencyUri)),
            (StandaloneA, MakeDoc(StandaloneA, "heroplansonly")));
        var tier = LuaFileClassifier.GetTier(DependencyUri, docs, s_fileHelper);
        Assert.Equal(LuaFileTier.Dependency, tier);
    }

    [Fact]
    public void GetTier_NotRequiredByAnyone_NotInLibraryDirectory_ReturnsStandalone()
    {
        var docs = MakeDocs(
            (StandaloneA, MakeDoc(StandaloneA)),
            (StandaloneB, MakeDoc(StandaloneB)));
        var tier = LuaFileClassifier.GetTier(StandaloneA, docs, s_fileHelper);
        Assert.Equal(LuaFileTier.Standalone, tier);
    }

    [Fact]
    public void GetTier_FileRequiresOthersButIsNotItselfRequiredByAnyone_ReturnsStandalone()
    {
        // Plan_A requires a library — but Plan_A itself is not required by anyone
        var docs = MakeDocs(
            (LibraryUri, MakeDoc(LibraryUri)),
            (StandaloneA, MakeDoc(StandaloneA, "pgstatemachine")));
        var tier = LuaFileClassifier.GetTier(StandaloneA, docs, s_fileHelper);
        Assert.Equal(LuaFileTier.Standalone, tier);
    }

    [Fact]
    public void GetTier_CaseInsensitiveUriMatch_ClassifiesCorrectly()
    {
        // URI lookup must be case-insensitive
        var docs = MakeDocs(
            (DependencyUri, MakeDoc(DependencyUri)),
            (StandaloneA, MakeDoc(StandaloneA, "heroplansonly")));
        var tier = LuaFileClassifier.GetTier(DependencyUri.ToUpperInvariant(), docs, s_fileHelper);
        Assert.Equal(LuaFileTier.Dependency, tier);
    }

    // ── GetSharedUris ─────────────────────────────────────────────────────────

    [Fact]
    public void GetSharedUris_EmptyDocuments_ReturnsEmptySet()
    {
        var result = LuaFileClassifier.GetSharedUris(MakeDocs(), s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void GetSharedUris_IncludesLibraryUris()
    {
        var docs = MakeDocs(
            (LibraryUri, MakeDoc(LibraryUri)),
            (StandaloneA, MakeDoc(StandaloneA)));
        var result = LuaFileClassifier.GetSharedUris(docs, s_fileHelper);
        Assert.Contains(LibraryUri, result);
    }

    [Fact]
    public void GetSharedUris_IncludesDependencyUris()
    {
        var docs = MakeDocs(
            (DependencyUri, MakeDoc(DependencyUri)),
            (StandaloneA, MakeDoc(StandaloneA, "heroplansonly")));
        var result = LuaFileClassifier.GetSharedUris(docs, s_fileHelper);
        Assert.Contains(DependencyUri, result);
    }

    [Fact]
    public void GetSharedUris_ExcludesStandaloneUris()
    {
        var docs = MakeDocs(
            (LibraryUri, MakeDoc(LibraryUri)),
            (StandaloneA, MakeDoc(StandaloneA, "pgstatemachine")),
            (StandaloneB, MakeDoc(StandaloneB)));
        var result = LuaFileClassifier.GetSharedUris(docs, s_fileHelper);
        Assert.DoesNotContain(StandaloneA, result);
        Assert.DoesNotContain(StandaloneB, result);
    }

    [Fact]
    public void GetSharedUris_IncludesBothLibraryAndDependency()
    {
        var docs = MakeDocs(
            (LibraryUri, MakeDoc(LibraryUri)),
            (DependencyUri, MakeDoc(DependencyUri)),
            (StandaloneA, MakeDoc(StandaloneA, "pgstatemachine", "heroplansonly")),
            (StandaloneB, MakeDoc(StandaloneB)));
        var result = LuaFileClassifier.GetSharedUris(docs, s_fileHelper);
        Assert.Contains(LibraryUri, result);
        Assert.Contains(DependencyUri, result);
        Assert.DoesNotContain(StandaloneA, result);
        Assert.DoesNotContain(StandaloneB, result);
    }

    [Fact]
    public void GetSharedUris_ResultSetIsCaseInsensitive()
    {
        var docs = MakeDocs(
            (LibraryUri, MakeDoc(LibraryUri)));
        var result = LuaFileClassifier.GetSharedUris(docs, s_fileHelper);
        Assert.Contains(LibraryUri.ToUpperInvariant(), result);
    }

    // ── IsLibraryUri ──────────────────────────────────────────────────────────

    [Fact]
    public void IsLibraryUri_UriContainingLibrarySegment_ReturnsTrue()
    {
        Assert.True(LuaFileClassifier.IsLibraryUri(LibraryUri));
        Assert.True(LuaFileClassifier.IsLibraryUri("file:///Data/Scripts/Library/pgstatemachine.lua"));
    }

    [Fact]
    public void IsLibraryUri_UriNotContainingLibrarySegment_ReturnsFalse()
    {
        Assert.False(LuaFileClassifier.IsLibraryUri(StandaloneA));
        Assert.False(LuaFileClassifier.IsLibraryUri(DependencyUri));
    }
}