// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaStorySymbolCollectionTest
{
    private const string Uri = "file:///ws/data/scripts/story/story_campaign.lua";

    private static async Task<DocumentIndex> ParseAsync(string lua,
        bool symbolsFlag = true, string? apiSchema = null)
    {
        var config = new StubConfigProvider(new LspConfiguration
        {
            Features = new FeatureFlags { Story = new StoryFeatureFlags { Symbols = symbolsFlag } }
        });
        var parser = new LuaGameDocumentParser(
            new LuaApiSchemaProvider(apiSchema is null ? [] : [apiSchema]),
            new FileHelper(new MockFileSystem()),
            NullLogger<LuaGameDocumentParser>.Instance,
            new LuaAnnotationRepository(),
            configProvider: config);
        return await parser.ParseAsync(Uri, lua, 1, CancellationToken.None);
    }

    [Fact]
    public async Task StoryModeEvents_TopLevelAssignment_KeysBecomeStoryEventReferences()
    {
        var index = await ParseAsync("""
                                     StoryModeEvents =
                                     {
                                         Rebel_M01_Start = State_Start,
                                         Rebel_M01_Done = State_Done
                                     }
                                     """);

        var refs = index.References.Where(r => r.ExpectedTypeName == "StoryEvent").ToList();
        Assert.Equal(["Rebel_M01_Start", "Rebel_M01_Done"], refs.Select(r => r.TargetId));
        Assert.Equal(2, refs[0].Line);
        Assert.Equal(4, refs[0].Column);
        Assert.Equal("Rebel_M01_Start".Length, refs[0].Length);
    }

    [Fact]
    public async Task StoryModeEvents_NestedInsideDefinitionsTable_IsAlsoCollected()
    {
        // Vanilla shape: the table sits inside an outer definitions table.
        var index = await ParseAsync("""
                                     Definitions =
                                     {
                                         StoryModeEvents =
                                         {
                                             ActI_Fondor_Rebels_00 = State_ActI_Fondor_Rebels_00
                                         }
                                     }
                                     """);

        var reference = Assert.Single(index.References, r => r.ExpectedTypeName == "StoryEvent");
        Assert.Equal("ActI_Fondor_Rebels_00", reference.TargetId);
    }

    [Fact]
    public async Task StoryEventCall_DefinesStoryNotificationSymbol()
    {
        var index = await ParseAsync("Story_Event(\"Land_On_Wayland\")");

        var symbol = Assert.Single(index.Symbols, s => s.TypeName == "StoryNotification");
        Assert.Equal("Land_On_Wayland", symbol.Id);
        var origin = Assert.IsType<FileOrigin>(symbol.Origin);
        Assert.Equal(0, origin.Line);
        Assert.Equal("Story_Event(\"".Length, origin.Column);
    }

    [Fact]
    public async Task StoryEventCall_SameIdTwice_DefinesOnlyOnce()
    {
        var index = await ParseAsync("Story_Event(\"Ping\")\nStory_Event(\"Ping\")");

        Assert.Single(index.Symbols, s => s.TypeName == "StoryNotification");
    }

    [Fact]
    public async Task CheckStoryFlag_WithXmlrefSchema_EmitsStoryFlagReference()
    {
        const string api = """
                           ---@param flagName string
                           ---@xmlref XmlObject:StoryFlag
                           function Check_Story_Flag(flagName) end
                           """;

        var index = await ParseAsync("if Check_Story_Flag(\"REBEL_TRAP_SET\") then end", apiSchema: api);

        var reference = Assert.Single(index.References, r => r.ExpectedTypeName == "StoryFlag");
        Assert.Equal("REBEL_TRAP_SET", reference.TargetId);
    }

    [Fact]
    public async Task SymbolsFlagOff_EmitsNoStorySymbols()
    {
        var index = await ParseAsync(
            "StoryModeEvents = { E = S }\nStory_Event(\"Ping\")", symbolsFlag: false);

        Assert.DoesNotContain(index.Symbols, s => s.TypeName == "StoryNotification");
        Assert.DoesNotContain(index.References, r => r.ExpectedTypeName == "StoryEvent");
    }

    private sealed class StubConfigProvider(LspConfiguration current) : ILspConfigurationProvider
    {
        public LspConfiguration Current => current;

        public void LoadFrom(object? initializationOptions)
        {
        }
    }
}
