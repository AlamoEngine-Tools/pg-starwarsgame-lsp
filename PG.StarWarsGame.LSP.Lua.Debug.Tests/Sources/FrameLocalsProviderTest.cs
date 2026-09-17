// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.Sources;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.Sources;

public sealed class FrameLocalsProviderTest
{
    private const string Script = """
                                  local top = 1
                                  function Tick(unit, dt)
                                      local speed = unit.Get_Speed()
                                      if speed > 0 then
                                          local boosted = speed * 2
                                          Move(boosted)
                                      end
                                      local speed = 0
                                      local after = 3
                                  end
                                  local function Helper(x)
                                      local inner = x
                                      return inner
                                  end
                                  """;

    private readonly IFrameLocalsProvider _provider = TestServices.Get<IFrameLocalsProvider>();

    [Fact]
    public void LocalsAt_LineInsideTick_HasParametersAndLocalsAboveIt()
    {
        var names = _provider.LocalsAt(Script, 6).Select(l => l.Name).ToList();

        Assert.Equal(["top", "speed", "boosted", "unit", "dt"], names);
    }

    [Fact]
    public void LocalsAt_MarksParameters()
    {
        var locals = _provider.LocalsAt(Script, 6);

        Assert.True(locals.Single(l => l.Name == "unit").IsParameter);
        Assert.False(locals.Single(l => l.Name == "speed").IsParameter);
    }

    [Fact]
    public void LocalsAt_LocalDeclaredOnTheCurrentLine_IsNotInScopeYet()
    {
        var names = _provider.LocalsAt(Script, 9).Select(l => l.Name);

        Assert.DoesNotContain("after", names);
    }

    [Fact]
    public void LocalsAt_ShadowedName_AppearsOnceAtItsInnermostPosition()
    {
        var names = _provider.LocalsAt(Script, 9).Select(l => l.Name).ToList();

        Assert.Single(names, n => n == "speed");
        Assert.Equal("speed", names.Last(n => n is "speed" or "boosted"));
    }

    [Fact]
    public void LocalsAt_InsideAnotherFunction_DoesNotSeeTicksLocals()
    {
        var names = _provider.LocalsAt(Script, 13).Select(l => l.Name).ToList();

        Assert.Contains("x", names);
        Assert.Contains("inner", names);
        Assert.DoesNotContain("unit", names);
        Assert.DoesNotContain("speed", names);
    }

    [Fact]
    public void LocalsAt_LineZeroOrNegative_IsEmpty()
    {
        Assert.Empty(_provider.LocalsAt(Script, 0));
        Assert.Empty(_provider.LocalsAt(Script, -4));
    }

    [Fact]
    public void LocalsAt_UnparsableText_StillReturnsWhatItCan()
    {
        var names = _provider.LocalsAt("local a = 1\nfunction (broken\nlocal b = 2", 3).Select(l => l.Name);

        Assert.Contains("a", names);
    }
}
