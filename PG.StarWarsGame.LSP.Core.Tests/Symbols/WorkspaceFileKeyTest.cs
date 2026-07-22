// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     The whole point of the key is that a file-symbol (built from the file's own path/name) and a
///     reference to it (built from a tag value) collapse to the SAME id despite separator, casing,
///     and DATA\XML-prefix differences. These tests pin that agreement.
/// </summary>
public sealed class WorkspaceFileKeyTest
{
    [Fact]
    public void XmlFileType_NormalizesSeparatorsCasingAndDataXmlPrefix()
    {
        var fromValue = WorkspaceFileKey.Create("StoryPlotManifest", "Story_Plots_Campaign_Empire.xml");
        var fromGameRootPath = WorkspaceFileKey.Create("StoryPlotManifest",
            "DATA\\XML\\story_plots_campaign_empire.xml");

        Assert.Equal("storyplotmanifest:story_plots_campaign_empire.xml", fromValue);
        Assert.Equal(fromValue, fromGameRootPath);
    }

    [Fact]
    public void XmlFileType_PreservesSubdirectories()
    {
        Assert.Equal("storyparser:conquests/loader/story_plots_loader.xml",
            WorkspaceFileKey.Create("StoryParser", "Conquests\\Loader\\Story_Plots_Loader.xml"));
    }

    [Fact]
    public void LuaScript_KeysByExtensionlessBaseName()
    {
        // The reference value is a bare name; the file-symbol side passes the filename with .lua.
        var fromReference = WorkspaceFileKey.Create("LuaScript", "Story_Rebel_Act_III");
        var fromFileName = WorkspaceFileKey.Create("LuaScript", "Story_Rebel_Act_III.lua");

        Assert.Equal("luascript:story_rebel_act_iii", fromReference);
        Assert.Equal(fromReference, fromFileName);
    }
}
