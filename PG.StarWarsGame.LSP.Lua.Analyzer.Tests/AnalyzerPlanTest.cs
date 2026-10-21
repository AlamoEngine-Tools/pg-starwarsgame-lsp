// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>The configuration one script instance's analyzer reads.</summary>
public sealed class AnalyzerPlanTest
{
    [Fact]
    public void TheConfiguration_CarriesTheLibrary_AndTheDisableList()
    {
        var plan = new AnalyzerPlan(["C:/cache/1/root"], ["D:/schema/lua/api.d.lua", "D:/schema/lua/pgdebug.d.lua"]);

        using var json = JsonDocument.Parse(plan.ToJson());
        var root = json.RootElement;

        var library = root.GetProperty("workspace").GetProperty("library").EnumerateArray().Select(e => e.GetString());
        Assert.Equal(["D:/schema/lua/api.d.lua", "D:/schema/lua/pgdebug.d.lua"], library);
        var disabled = root.GetProperty("diagnostics").GetProperty("disable").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.Contains("undefined-global", disabled);
        Assert.Contains("unresolved-require", disabled);
    }
}