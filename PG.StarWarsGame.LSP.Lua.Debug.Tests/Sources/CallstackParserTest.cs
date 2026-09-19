// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.Sources;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.Sources;

public sealed class CallstackParserTest
{
    private readonly ICallstackParser _parser = TestServices.Get<ICallstackParser>();

    [Fact]
    public void ParseEntry_FiveFields_ReadsEachOne()
    {
        var frame = _parser.ParseEntry(3, "Data\\Scripts\\GameObject\\Hero.lua:30:Lua:global:Tick");

        Assert.NotNull(frame);
        Assert.Equal(3, frame.Level);
        Assert.Equal("Data\\Scripts\\GameObject\\Hero.lua", frame.Source);
        Assert.Equal(30, frame.Line);
        Assert.Equal("Lua", frame.What);
        Assert.Equal("global", frame.NameWhat);
        Assert.Equal("Tick", frame.Name);
        Assert.Equal("Tick", frame.DisplayName);
    }

    [Fact]
    public void ParseEntry_SourceWithDriveLetter_KeepsTheColonInTheSource()
    {
        var frame = _parser.ParseEntry(0, "C:\\Game\\Data\\Scripts\\A.lua:5:main::");

        Assert.NotNull(frame);
        Assert.Equal("C:\\Game\\Data\\Scripts\\A.lua", frame.Source);
        Assert.Equal(5, frame.Line);
        Assert.Equal("main", frame.What);
        Assert.Equal("", frame.NameWhat);
        Assert.Equal("", frame.Name);
        Assert.Equal("<main>", frame.DisplayName);
    }

    [Fact]
    public void ParseEntry_LeadingAt_IsStripped()
    {
        var frame = _parser.ParseEntry(0, "@Data\\Scripts\\A.lua:5:main::");

        Assert.Equal("Data\\Scripts\\A.lua", frame!.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no colons at all")]
    [InlineData("A.lua:5:main")]
    [InlineData("A.lua:five:Lua:global:Tick")]
    public void ParseEntry_WrongShape_ReturnsNull(string entry)
    {
        Assert.Null(_parser.ParseEntry(0, entry));
    }

    [Fact]
    public void ParseStack_WireOrder_IsReversedToTopFirstWithWireLevels()
    {
        var frames = _parser.ParseStack(
            ["A.lua:1:main::", "A.lua:12:Lua:global:Run", "B.lua:30:Lua:field:Tick"], false);

        Assert.Equal([2, 1, 0], frames.Select(f => f.Level));
        Assert.Equal(["Tick", "Run", "<main>"], frames.Select(f => f.DisplayName));
    }

    [Fact]
    public void ParseStack_DuplicateOutermost_DroppedOnlyWhenAsked()
    {
        string[] entries = ["A.lua:1:main::", "A.lua:1:main::", "A.lua:12:Lua:global:Run"];

        var kept = _parser.ParseStack(entries, false);
        var dropped = _parser.ParseStack(entries, true);

        Assert.Equal(3, kept.Count);
        Assert.Equal(2, dropped.Count);
        Assert.Equal([2, 1], dropped.Select(f => f.Level));
    }

    [Fact]
    public void ParseStack_DistinctOutermost_IsNeverDropped()
    {
        var frames = _parser.ParseStack(["A.lua:1:main::", "A.lua:2:main::"], true);

        Assert.Equal(2, frames.Count);
    }

    [Fact]
    public void ParseStack_UnparsableEntry_KeepsItsLevelWithAnEmptySource()
    {
        var frames = _parser.ParseStack(["garbage", "A.lua:12:Lua:global:Run"], false);

        Assert.Equal(2, frames.Count);
        Assert.Equal(0, frames[1].Level);
        Assert.Equal("", frames[1].Source);
        Assert.Equal("garbage", frames[1].Raw);
    }

    [Fact]
    public void ParseStack_Empty_IsEmpty()
    {
        Assert.Empty(_parser.ParseStack([], true));
    }
}