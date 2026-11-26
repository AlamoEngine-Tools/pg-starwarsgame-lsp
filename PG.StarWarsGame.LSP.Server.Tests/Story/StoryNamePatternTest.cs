// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Story;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

/// <summary>
///     The story graph's event-name pattern, read as the search plate reads it in the webview: plain
///     text, wildcards or a regular expression, all without case, all found anywhere in the name.
/// </summary>
public class StoryNamePatternTest
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("text", "")]
    [InlineData("wildcard", "")]
    [InlineData("regex", "")]
    public void AnEmptyPatternAnswersEverything(string? mode, string pattern)
    {
        Assert.True(StoryNamePattern.Compile(pattern, mode)("Act1_Rebel_Intro"));
    }

    [Theory]
    [InlineData("rebel", true)]
    [InlineData("REBEL_IN", true)]
    [InlineData("act?", false)]
    [InlineData("a.t1", false)]
    public void PlainTextIsFoundAnywhereWithoutCase(string pattern, bool found)
    {
        Assert.Equal(found, StoryNamePattern.Compile(pattern, "text")("Act1_Rebel_Intro"));
    }

    // Only * and ? are special. Every other character means itself, so a dot in a name does not
    // quietly widen the filter - the webview's lists read it the same way.
    [Theory]
    [InlineData("act?_*_intro", true)]
    [InlineData("rebel", true)]
    [InlineData("a.t1", false)]
    [InlineData("act1_(rebel)", false)]
    [InlineData("*", true)]
    public void WildcardsAreStarAndQuestionMarkOnly(string pattern, bool found)
    {
        Assert.Equal(found, StoryNamePattern.Compile(pattern, "wildcard")("Act1_Rebel_Intro"));
    }

    [Theory]
    [InlineData("^act\\d_rebel", true)]
    [InlineData("intro$", true)]
    [InlineData("^rebel", false)]
    [InlineData("(rebel|empire)_intro", true)]
    public void ARegularExpressionIsFoundAnywhereWithoutCase(string pattern, bool found)
    {
        Assert.Equal(found, StoryNamePattern.Compile(pattern, "regex")("Act1_Rebel_Intro"));
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[a-")]
    [InlineData("*")]
    public void ARegularExpressionThatDoesNotCompileAnswersNothing(string pattern)
    {
        Assert.False(StoryNamePattern.Compile(pattern, "regex")("Act1_Rebel_Intro"));
    }

    // A pattern is typed by a reader and run against every event of a campaign; one that takes
    // too long on a name answers "no" for that name rather than holding the request. The answer
    // would be "no" either way - this name does not match - so what is tested is that it comes back.
    [Fact]
    public void ARegularExpressionThatRunsTooLongAnswersNothing()
    {
        var catastrophic = StoryNamePattern.Compile("^(a+)+$", "regex");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(catastrophic(new string('a', 40) + "!"));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Took {clock.Elapsed}");
    }

    [Fact]
    public void AModeItDoesNotKnowIsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() => StoryNamePattern.Compile("x", "glob"));

        Assert.Contains("glob", refused.Message);
    }
}
