// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Parsing;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaFrameScopeTest
{
    private const string Script = """
                                  local top = 1
                                  function Tick(unit, dt)
                                      local speed = unit.Get_Speed()
                                      if speed > 0 then
                                          local boosted = speed * 2
                                          Move(boosted)
                                      end
                                      local after = 3
                                  end
                                  """;

    [Fact]
    public void LocalsAt_InsideFunctionBody_ListsParametersAndLocalsDeclaredAbove()
    {
        var document = ParsedLuaDocument.Parse(Script);

        var locals = LuaFrameScope.LocalsAt(document.Tree, Script, 5, 0);

        Assert.Contains(new LuaFrameLocal("unit", true), locals);
        Assert.Contains(new LuaFrameLocal("dt", true), locals);
        Assert.Contains(new LuaFrameLocal("speed", false), locals);
        Assert.Contains(new LuaFrameLocal("boosted", false), locals);
        Assert.Contains(new LuaFrameLocal("top", false), locals);
        Assert.DoesNotContain(locals, l => l.Name == "after");
    }

    [Fact]
    public void LocalsAt_OutsideTheFunction_HasNoParameters()
    {
        var document = ParsedLuaDocument.Parse(Script);

        var locals = LuaFrameScope.LocalsAt(document.Tree, Script, 0, 0);

        Assert.DoesNotContain(locals, l => l.IsParameter);
        Assert.DoesNotContain(locals, l => l.Name == "speed");
    }
}