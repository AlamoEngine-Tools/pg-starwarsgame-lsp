// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Guards the plot-file reference tags across the campaign story chain. Their value is a FILE
///     reference (a plot manifest, story thread, or Lua script) - not a game-object name - so they
///     are declared <see cref="ReferenceKind.WorkspaceFile" /> with a <c>referenceType</c> naming
///     the target file-type. This drives go-to / find-references / rename to the referenced file.
///     Loads the real schema/eaw/ tag files.
/// </summary>
public sealed class EawSchemaCampaignStoryTagTest
{
    private static readonly IReadOnlyList<RawTagDefinition> CampaignTags = LoadTags("Campaign.yaml");
    private static readonly IReadOnlyList<RawTagDefinition> ManifestTags = LoadTags("StoryPlotManifest.yaml");
    private static readonly IReadOnlyList<RawTagDefinition> ConstantsTags = LoadTags("GameConstants.yaml");

    [Theory]
    [InlineData("Rebel_Story_Name")]
    [InlineData("Empire_Story_Name")]
    [InlineData("Underworld_Story_Name")]
    [InlineData("Story_Name")]
    public void CampaignStoryName_IsAWorkspaceFileReferenceToAPlotManifest(string tagName)
    {
        var tag = CampaignTags.FirstOrDefault(t =>
            string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(tag);
        Assert.Equal(ReferenceKind.WorkspaceFile, tag!.ReferenceKind);
        Assert.Equal("StoryPlotManifest", tag.ReferenceType);
    }

    [Fact]
    public void GenericStoryName_IsAdditive()
    {
        var tag = CampaignTags.First(t => string.Equals(t.Tag, "Story_Name", StringComparison.OrdinalIgnoreCase));
        Assert.True(tag.MultipleAllowed, "Story_Name is additive and may appear multiple times.");
    }

    [Theory]
    [InlineData("Active_Plot", "StoryParser")]
    [InlineData("Suspended_Plot", "StoryParser")]
    [InlineData("Lua_Script", "LuaScript")]
    public void ManifestPlotTag_IsAWorkspaceFileReference(string tagName, string expectedReferenceType)
    {
        var tag = ManifestTags.FirstOrDefault(t =>
            string.Equals(t.Tag, tagName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(tag);
        Assert.Equal(ReferenceKind.WorkspaceFile, tag!.ReferenceKind);
        Assert.Equal(expectedReferenceType, tag.ReferenceType);
    }

    /// <summary>
    ///     The debug hot-key that loads a plot is the same kind of reference as everything above,
    ///     and was the one that had no <c>referenceKind</c> at all - so its value was never
    ///     indexed: no go-to, no unresolved-reference validation, no completion.
    /// </summary>
    /// <remarks>
    ///     Measured, not inferred: the value in vanilla is
    ///     <c>Story_Plots_UM00_CIN_Test.xml</c>, and that file's root is
    ///     <c>&lt;Story_Mode_Plots&gt;</c> carrying <c>Active_Plot</c> and <c>Lua_Script</c> - a
    ///     plot MANIFEST, spelled exactly as <c>Empire_Story_Name</c> spells its own. So it takes
    ///     the manifest file-type, not the thread one.
    /// </remarks>
    [Fact]
    public void DebugHotKeyLoadMapScript_IsAWorkspaceFileReferenceToAPlotManifest()
    {
        var tag = ConstantsTags.FirstOrDefault(t =>
            string.Equals(t.Tag, "Debug_Hot_Key_Load_Map_Script", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(tag);
        Assert.Equal(ReferenceKind.WorkspaceFile, tag!.ReferenceKind);
        Assert.Equal("StoryPlotManifest", tag.ReferenceType);
    }

    // Its sibling points at a .ted map and has always been right; kept beside it so the pair stays
    // visibly a pair, and so that a future edit to one is measured against the other.
    [Fact]
    public void DebugHotKeyLoadMap_StillPointsAtAMapFile()
    {
        var tag = ConstantsTags.FirstOrDefault(t =>
            string.Equals(t.Tag, "Debug_Hot_Key_Load_Map", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(tag);
        Assert.Equal(ReferenceKind.MapFile, tag!.ReferenceKind);
    }

    private static IReadOnlyList<RawTagDefinition> LoadTags(string tagFile)
    {
        var path = FindTagFile(tagFile)
                   ?? throw new InvalidOperationException(
                       $"schema/eaw/tags/{tagFile} not found - ensure the schema submodule is checked out.");
        return YamlSchemaParser.ParseTagFile(File.ReadAllText(path));
    }

    private static string? FindTagFile(string tagFile)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaCampaignStoryTagTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", "tags", tagFile);
                return File.Exists(candidate) ? candidate : null;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
