// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Persistence;

namespace PG.StarWarsGame.LSP.Core.Tests.Persistence;

/// <summary>
///     The key a persisted document uses to name a file: project-relative, folded the way the game
///     folds, then hashed. Nothing stored carries a path.
/// </summary>
public sealed class DocumentKeyTest
{
    // ── the fold ─────────────────────────────────────────────────────────────

    // The engine treats both characters as separators on every host - PetroglyphFileSystem
    // "simulates Windows-like behavior for all its public methods on Linux" - so a path written
    // either way is one path, and must be one key.
    [Fact]
    public void Fold_TreatsBothSeparatorsAsOne()
    {
        Assert.Equal(
            DocumentKey.Fold(@"data\xml\story_tutorial_i.xml"),
            DocumentKey.Fold("data/xml/story_tutorial_i.xml"));
    }

    // The game is case-insensitive: FOO.xml and foo.xml are the same asset by design. Keys have to
    // agree with the game, not with the host filesystem.
    [Fact]
    public void Fold_IsCaseInsensitive()
    {
        Assert.Equal(
            DocumentKey.Fold("Data/XML/Story_Tutorial_I.xml"),
            DocumentKey.Fold("data/xml/story_tutorial_i.xml"));
    }

    // Upper, not lower, because that is the fold PetroglyphFileSystem.PathCharEqual applies
    // (char.ToUpperInvariant). Invariant, so a Turkish-locale machine derives the same key.
    [Fact]
    public void Fold_UsesTheInvariantUppercaseTheEngineCompareWith()
    {
        Assert.Equal("DATA/XML/FILE.XML", DocumentKey.Fold("data/xml/file.xml"));
    }

    [Theory]
    [InlineData("./data/xml/a.xml")]
    [InlineData("/data/xml/a.xml")]
    [InlineData("data/xml/a.xml  ")]
    [InlineData(@"\data\xml\a.xml")]
    public void Fold_IgnoresLeadingAndTrailingNoise(string written)
    {
        Assert.Equal(DocumentKey.Fold("data/xml/a.xml"), DocumentKey.Fold(written));
    }

    // ── the hash ─────────────────────────────────────────────────────────────

    // A known answer from RFC 4122, so this is a real name-based v5 UUID rather than a homemade
    // digest: determinism has to hold across machines, runtimes and releases, and only a specified
    // algorithm promises that.
    [Fact]
    public void NameBased_MatchesTheRfcTestVector()
    {
        var dns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        Assert.Equal(
            Guid.Parse("74738ff5-5367-5958-9aee-98fffdcd1876"),
            DocumentKey.NameBased(dns, "www.example.org"));
    }

    [Fact]
    public void NameBased_StampsVersionFiveAndTheRfcVariant()
    {
        var key = DocumentKey.Of("data/xml/a.xml");
        var bytes = key.ToByteArray(true); // big-endian, as the RFC lays the fields out

        Assert.Equal(0x50, bytes[6] & 0xF0);
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }

    [Fact]
    public void Of_IsDeterministic()
    {
        Assert.Equal(DocumentKey.Of("data/xml/a.xml"), DocumentKey.Of("data/xml/a.xml"));
    }

    [Fact]
    public void Of_FollowsTheFold()
    {
        Assert.Equal(DocumentKey.Of(@"Data\XML\A.xml"), DocumentKey.Of("data/xml/a.xml"));
    }

    [Fact]
    public void Of_DifferentPathsDiffer()
    {
        Assert.NotEqual(DocumentKey.Of("data/xml/a.xml"), DocumentKey.Of("data/xml/b.xml"));
    }

    // Two files with the same name in different directories are two assets, and the base name that
    // the layout sidecar keys on today cannot tell them apart. The relative path can.
    [Fact]
    public void Of_SameNameInDifferentDirectories_AreDifferentKeys()
    {
        Assert.NotEqual(
            DocumentKey.Of("data/xml/story/a.xml"), DocumentKey.Of("data/xml/campaign/a.xml"));
    }

    // ── composite identities ─────────────────────────────────────────────────

    // An identity made of several parts is hashed WHOLE, rather than kept as a key plus the rest of
    // the parts in the clear. That is what the old sidecars did by concatenating - file, event -
    // and half-hashing it would leak the half that was not hashed while pretending otherwise.
    [Fact]
    public void Composite_IsDeterministic()
    {
        Assert.Equal(
            DocumentKey.Composite("data/xml/a.xml", "Start"),
            DocumentKey.Composite("data/xml/a.xml", "Start"));
    }

    [Fact]
    public void Composite_FoldsEveryPart()
    {
        Assert.Equal(
            DocumentKey.Composite(@"Data\XML\A.xml", "START"),
            DocumentKey.Composite("data/xml/a.xml", "start"));
    }

    [Fact]
    public void Composite_OrderMatters()
    {
        Assert.NotEqual(DocumentKey.Composite("a", "b"), DocumentKey.Composite("b", "a"));
    }

    // The trap in any join: two different identities must not flatten into one string. A separator
    // alone does not settle it, because a PART can contain the separator - event names are free
    // text out of the mod's own XML. Length prefixes are what make the split unambiguous whatever
    // the parts contain, so this uses parts that carry the separator; without them these two are
    // the same key.
    [Fact]
    public void Composite_CannotBeConfusedByAPartContainingTheSeparator()
    {
        Assert.NotEqual(DocumentKey.Composite("a|b", "c"), DocumentKey.Composite("a", "b|c"));
        Assert.NotEqual(DocumentKey.Composite("a|", "b"), DocumentKey.Composite("a", "|b"));
    }

    [Fact]
    public void Composite_CannotBeConfusedByWhereTheSplitFalls()
    {
        Assert.NotEqual(DocumentKey.Composite("ab", "c"), DocumentKey.Composite("a", "bc"));
        Assert.NotEqual(DocumentKey.Composite("a/b", "c"), DocumentKey.Composite("a", "b/c"));
    }

    [Fact]
    public void Composite_DiffersFromTheSinglePartKey()
    {
        Assert.NotEqual(DocumentKey.Of("data/xml/a.xml"), DocumentKey.Composite("data/xml/a.xml", ""));
    }

    // ── project-relative ─────────────────────────────────────────────────────

    [Fact]
    public void Relative_IsTakenFromTheProjectDirectory()
    {
        Assert.Equal(
            "data/xml/a.xml",
            DocumentKey.Relative("C:/mods/mymod", "C:/mods/mymod/data/xml/a.xml"));
    }

    // The URIs the server holds are lowercased absolute file:// strings; a key derived from one has
    // to come out the same as a key derived from the real path, which the fold is what guarantees.
    [Fact]
    public void Relative_AcceptsAFileUri()
    {
        Assert.Equal(
            DocumentKey.Of(DocumentKey.Relative("C:/mods/mymod", "C:/mods/MyMod/Data/XML/A.xml")!),
            DocumentKey.Of(DocumentKey.Relative("C:/mods/mymod", "file:///c:/mods/mymod/data/xml/a.xml")!));
    }

    // Outside the project there is no relative form, and inventing one would key a file by
    // something that changes with the machine.
    [Fact]
    public void Relative_OutsideTheProject_IsNull()
    {
        Assert.Null(DocumentKey.Relative("C:/mods/mymod", "C:/elsewhere/a.xml"));
    }
}
