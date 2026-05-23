// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class StoryParamUnknownSlotHandlerTest
{
    private static readonly StoryParamUnknownSlotHandler Sut = new();

    [Fact]
    public void Null_def_with_non_empty_value_emits_warning_with_tag_and_event_names()
    {
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 1, null, "extra");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Event_Param2", d.Message);
        Assert.Contains("MY_EVENT", d.Message);
    }

    [Fact]
    public void Null_def_with_empty_value_emits_no_diagnostics()
    {
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 1, null, "");
        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Non_null_def_emits_no_diagnostics()
    {
        var def = new ParamDefinition { Position = 0, ValueType = XmlValueType.Int };
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0, def, "extra");
        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Reward_param_uses_Reward_Param_prefix()
    {
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_REWARD", true, 2, null, "extra");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Contains("Reward_Param3", d.Message);
    }
}