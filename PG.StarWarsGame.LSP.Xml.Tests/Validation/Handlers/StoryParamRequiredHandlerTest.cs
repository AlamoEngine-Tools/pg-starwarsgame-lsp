// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class StoryParamRequiredHandlerTest
{
    private static readonly StoryParamRequiredHandler Sut = new();

    private static ParamDefinition MakeDef(int pos, bool optional)
    {
        return new ParamDefinition { Position = pos, ValueType = XmlValueType.Int, Optional = optional };
    }

    [Fact]
    public void Required_param_with_empty_value_emits_warning_with_event_and_tag_names()
    {
        var def = MakeDef(0, false);
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0, def, "");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("MY_EVENT", d.Message);
        Assert.Contains("Event_Param1", d.Message);
    }

    [Fact]
    public void Optional_param_with_empty_value_emits_no_diagnostics()
    {
        var def = MakeDef(0, true);
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0, def, "");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Required_param_with_non_empty_value_emits_no_diagnostics()
    {
        var def = MakeDef(0, false);
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0, def, "42");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Required_reward_param_uses_Reward_Param_prefix()
    {
        var def = MakeDef(1, false);
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_REWARD", true, 1, def, "");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Contains("Reward_Param2", d.Message);
    }
}