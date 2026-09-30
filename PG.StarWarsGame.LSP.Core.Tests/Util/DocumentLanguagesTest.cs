// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Util;

public sealed class DocumentLanguagesTest
{
    [Theory]
    [InlineData("file:///d%3A/mod/data/xml/story_plot.xml", "xml")]
    [InlineData("file:///d:/mod/Data/XML/STORY_PLOT.XML", "xml")]
    [InlineData("file:///d%3A/mod/data/scripts/story/plot.lua", "lua")]
    [InlineData("file:///d%3A/mod/data/text/dialog.txt", "plaintext")]
    [InlineData("file:///d%3A/mod/data/xml/story_plot.xml?version=2", "xml")]
    [InlineData(@"d:\mod\data\xml\story_plot.xml", "xml")]
    [InlineData("file:///d%3A/mod/readme", "plaintext")]
    public void LanguageIdOf_FollowsTheExtension(string uriOrPath, string language)
    {
        Assert.Equal(language, DocumentLanguages.LanguageIdOf(uriOrPath));
    }
}
