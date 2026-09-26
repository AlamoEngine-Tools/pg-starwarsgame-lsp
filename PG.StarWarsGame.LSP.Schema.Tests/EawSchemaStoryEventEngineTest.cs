// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Pins StoryEventType parameters to what each engine event class parses, measured against
///     the 2018 build's event classes. Loads the real schema/eaw/ YAML file.
/// </summary>
public sealed class EawSchemaStoryEventEngineTest
{
    private const string EventFile = "enums/StoryEventType.yaml";

    private static readonly RawEnumDefinition Events =
        YamlSchemaParser.ParseEnumFile(EawSchemaRepo.Read(EventFile));

    // The class has a working default or an explicit "any" branch for an empty value.
    [Theory]
    [InlineData("STORY_DESTROY_BASE", 0)]
    [InlineData("STORY_CONSTRUCT_LEVEL", 2)]
    [InlineData("STORY_CORRUPTION_CHANGED", 1)]
    [InlineData("STORY_LOAD_TACTICAL_MAP", 1)]
    [InlineData("STORY_SELECT_PLANET", 0)]
    [InlineData("STORY_ZOOM_INTO_PLANET", 0)]
    [InlineData("STORY_ZOOM_OUT_PLANET", 0)]
    [InlineData("STORY_BEGIN_ERA", 0)]
    public void Param_IsOptional(string evt, int position)
    {
        Assert.True(Param(evt, position).Optional, $"{evt} param {position} should be optional.");
    }

    // Without these the event can never fire.
    [Theory]
    [InlineData("STORY_DEPLOY", 1)]
    [InlineData("STORY_UNIT_PROXIMITY", 1)]
    [InlineData("STORY_UNIT_PROXIMITY", 2)]
    public void Param_IsRequired(string evt, int position)
    {
        Assert.False(Param(evt, position).Optional, $"{evt} param {position} should be required.");
    }

    // The class splits the value and matches any entry.
    [Theory]
    [InlineData("STORY_LAND_ON", 0)]
    [InlineData("STORY_DESTROY", 1)]
    [InlineData("STORY_TACTICAL_DESTROY", 1)]
    [InlineData("STORY_CORRUPTION_INCREASED", 0)]
    [InlineData("STORY_BUY_BLACK_MARKET", 0)]
    [InlineData("STORY_GALACTIC_SABOTAGE", 0)]
    [InlineData("STORY_ZOOM_INTO_PLANET", 0)]
    [InlineData("STORY_ZOOM_OUT_PLANET", 0)]
    [InlineData("STORY_OPEN_CORRUPTION", 0)]
    [InlineData("STORY_SPEECH_DONE", 0)]
    public void Param_IsAList(string evt, int position)
    {
        Assert.Equal(XmlValueType.NameReferenceList, Param(evt, position).ValueType);
    }

    // Shares STORY_TECH_LEVEL's parser: param 0 is the era, read with atoi.
    [Fact]
    public void BeginEra_TakesTheEraNumber()
    {
        Assert.Equal(XmlValueType.Int, Param("STORY_BEGIN_ERA", 0).ValueType);
    }

    // Raised with the attacked hardpoint's own name, not an object type.
    [Fact]
    public void AttackHardpoint_NamesAHardpoint_NotAnObjectType()
    {
        Assert.NotEqual("GameObjectType", Param("STORY_ATTACK_HARDPOINT", 0).ReferenceType);
    }

    // Converted with the difficulty type converter: a name, not a number.
    [Fact]
    public void DifficultyLevel_IsAName()
    {
        Assert.NotEqual(XmlValueType.Int, Param("STORY_DIFFICULTY_LEVEL", 0).ValueType);
    }

    [Fact]
    public void BuyBlackMarket_ReferencesBlackMarketItems()
    {
        Assert.Equal("BlackMarketItem", Param("STORY_BUY_BLACK_MARKET", 0).ReferenceType);
    }

    // An alias of STORY_CORRUPTION_CHANGED: same class, same params.
    [Fact]
    public void CorruptionIncreased_TakesTheCorruptionType()
    {
        var param = Param("STORY_CORRUPTION_INCREASED", 1);
        Assert.Equal("CorruptionType", param.EnumName);
        Assert.True(param.Optional);
    }

    [Fact]
    public void EventFile_IsAscii()
    {
        var text = EawSchemaRepo.Read(EventFile);
        var offending = text.Select((c, i) => (c, i)).Where(t => t.c > 127 && t.c != '﻿').ToList();

        Assert.True(offending.Count == 0,
            $"Non-ASCII characters at offsets {string.Join(", ", offending.Select(t => t.i).Take(5))}.");
    }

    private static RawParamDefinition Param(string evt, int position)
    {
        var value = Events.Values.FirstOrDefault(v => v.Name == evt);
        Assert.NotNull(value);
        var param = value.Params?.FirstOrDefault(p => p.Position == position);
        Assert.NotNull(param);
        return param;
    }
}