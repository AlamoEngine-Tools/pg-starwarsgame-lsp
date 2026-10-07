// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>The analyzer's configuration, from the project's layers.</summary>
public sealed class EmmyrcWriterTest
{
    private static ProjectLayer Layer(int rank, string name, params string[] scriptRoots)
    {
        return new ProjectLayer(rank, name, [], scriptRoots, [], [], null);
    }

    private static readonly IReadOnlyList<ProjectLayer> Layers =
    [
        Layer(0, "Empire at War", "C:/Games/EaW/Data/Scripts"),
        Layer(1, "Forces of Corruption", "C:/Games/FoC/Data/Scripts"),
        Layer(2, "My Mod", "D:/Mods/MyMod/Data/Scripts")
    ];

    [Fact]
    public void TheLeafIsTheWorkspace_LowerLayersAndTheStubsAreTheLibrary()
    {
        var plan = EmmyrcWriter.Plan(Layers, "D:/schema/lua");

        Assert.Equal(["D:/Mods/MyMod/Data/Scripts"], plan.WorkspaceRoots);
        Assert.Equal(["C:/Games/FoC/Data/Scripts", "C:/Games/EaW/Data/Scripts", "D:/schema/lua"], plan.Library);
    }

    [Fact]
    public void TheConfiguration_CarriesTheLibrary_TheEncoding_AndTheDisableList()
    {
        using var json = JsonDocument.Parse(EmmyrcWriter.Plan(Layers, "D:/schema/lua").ToJson());
        var root = json.RootElement;

        var library = root.GetProperty("workspace").GetProperty("library").EnumerateArray().Select(e => e.GetString());
        Assert.Contains("D:/schema/lua", library);
        // 2 EaW and 6 FoC scripts are Windows-1252; the default UTF-8 drops them without a word.
        Assert.Equal("windows-1252", root.GetProperty("workspace").GetProperty("encoding").GetString());
        var disabled = root.GetProperty("diagnostics").GetProperty("disable").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        // Global scope is ours: the analyzer sees one namespace, the game does not.
        Assert.Contains("undefined-global", disabled);
        Assert.Contains("unresolved-require", disabled);
    }

    [Fact]
    public void NoScriptsInTheLeaf_NothingToAnalyze()
    {
        var plan = EmmyrcWriter.Plan([Layer(0, "EaW", "C:/Games/EaW/Data/Scripts"), Layer(1, "Mod")], null);

        Assert.Empty(plan.WorkspaceRoots);
    }

    [Fact]
    public void NoStubs_TheLibraryIsTheLowerLayersAlone()
    {
        var plan = EmmyrcWriter.Plan(Layers, null);

        Assert.DoesNotContain(plan.Library, l => l.Contains("schema", StringComparison.Ordinal));
    }
}
