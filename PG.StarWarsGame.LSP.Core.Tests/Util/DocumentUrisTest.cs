// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Util;

/// <summary>
///     The fold that used to live in the value.
/// </summary>
/// <remarks>
///     The hazard this guards is quiet: a URI comparison or a dictionary that forgets to fold does
///     not throw, it simply fails to find a document that is right there. Most fixtures write URIs
///     in one consistent case, so the suite can be green while every mixed-case lookup in the field
///     misses - which is why these tests deliberately vary case at every boundary.
/// </remarks>
public sealed class DocumentUrisTest
{
    private static FileHelper Build()
    {
        return new FileHelper(new MockFileSystem());
    }

    // ── the rule ─────────────────────────────────────────────────────────────

    [Fact]
    public void Same_IgnoresCase()
    {
        Assert.True(DocumentUris.Same("file:///c:/data/units.xml", "file:///C:/Data/Units.XML"));
    }

    [Fact]
    public void Same_StillDistinguishesDifferentFiles()
    {
        Assert.False(DocumentUris.Same("file:///c:/data/units.xml", "file:///c:/data/units2.xml"));
    }

    [Fact]
    public void StartsWith_AndContains_Fold()
    {
        Assert.True(DocumentUris.StartsWith("file:///C:/Mod/Data/XML/a.xml", "file:///c:/mod/data/"));
        Assert.True(DocumentUris.Contains("file:///C:/Mod/Data/XML/AI/a.xml", "/ai/"));
    }

    // ── the collections built with it ────────────────────────────────────────

    [Fact]
    public void Comparer_MakesADictionaryFindEitherSpelling()
    {
        var map = new Dictionary<string, int>(DocumentUris.Comparer)
        {
            ["file:///C:/Data/Units.xml"] = 1
        };

        Assert.True(map.ContainsKey("file:///c:/data/units.xml"));
        // ...and it does not grow a second entry for the same file.
        map["file:///c:/DATA/units.XML"] = 2;
        Assert.Equal(2, Assert.Single(map).Value);
    }

    // ── the value keeps the name ─────────────────────────────────────────────

    // The point of moving the fold out of the value: the real name is still there afterwards, which
    // is what a case-sensitive filesystem needs to open the file at all.
    [Fact]
    public void NormalizeUri_KeepsTheNameItWasGiven()
    {
        var helper = Build();

        Assert.Equal(
            "file:///home/lukas/Mods/MyMod/Data/XML/Units.xml",
            helper.NormalizeUri("/home/lukas/Mods/MyMod/Data/XML/Units.xml"));
    }

    // Two files that a case-sensitive host really can hold at once stay two values, even though the
    // engine would call them one asset. Which of those two questions is being asked is now visible
    // at the call site rather than decided for everybody by the normalizer.
    [Fact]
    public void NormalizeUri_TwoSpellingsStayDistinctValuesButCompareEqual()
    {
        var helper = Build();

        var upper = helper.NormalizeUri("/home/lukas/mod/Units.xml");
        var lower = helper.NormalizeUri("/home/lukas/mod/units.xml");

        Assert.NotEqual(upper, lower);
        Assert.True(DocumentUris.Same(upper, lower));
    }
}
