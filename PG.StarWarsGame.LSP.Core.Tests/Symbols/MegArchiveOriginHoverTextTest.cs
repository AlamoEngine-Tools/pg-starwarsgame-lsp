// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class MegArchiveOriginHoverTextTest
{
    [Fact]
    public void Describe_ContainsArchiveFileNameNotFullPath()
    {
        var origin = new MegArchiveOrigin(@"C:\game\FoC_Art.meg", "DATA/ART/TEXTURES/FOO.TGA", null, null);
        var text = MegArchiveOriginHoverText.Describe(origin);
        Assert.Contains("FoC_Art.meg", text);
        Assert.DoesNotContain(@"C:\game\", text);
    }

    [Fact]
    public void Describe_ContainsInternalPath()
    {
        var origin = new MegArchiveOrigin(@"C:\game\FoC_Art.meg", "DATA/ART/TEXTURES/FOO.TGA", null, null);
        var text = MegArchiveOriginHoverText.Describe(origin);
        Assert.Contains("DATA/ART/TEXTURES/FOO.TGA", text);
    }

    [Fact]
    public void Describe_ContainsPackedEmoji()
    {
        var origin = new MegArchiveOrigin(@"C:\game\FoC_Art.meg", "DATA/ART/TEXTURES/FOO.TGA", null, null);
        var text = MegArchiveOriginHoverText.Describe(origin);
        Assert.Contains("📦", text);
    }

    [Fact]
    public void Describe_MentionsReadOnly()
    {
        var origin = new MegArchiveOrigin("data.meg", "units.xml", null, null);
        var text = MegArchiveOriginHoverText.Describe(origin);
        Assert.Contains("read-only", text, StringComparison.OrdinalIgnoreCase);
    }
}
