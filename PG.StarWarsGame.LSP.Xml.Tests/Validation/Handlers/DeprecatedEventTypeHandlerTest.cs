// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class DeprecatedEventTypeHandlerTest
{
    private static readonly DeprecatedEventTypeHandler Sut = new();

    private static StoryEventFact MakeFact(bool deprecated, bool isReward = false)
    {
        return new StoryEventFact("file:///test.xml", 1, 0, 0, "OLD_EVENT", isReward,
            deprecated ? new EnumValueDefinition { Name = "OLD_EVENT", Deprecated = true } : null);
    }

    [Fact]
    public void Deprecated_event_emits_warning_containing_type_name()
    {
        var fact = new StoryEventFact("file:///test.xml", 1, 0, 0, "OLD_EVENT", false,
            new EnumValueDefinition { Name = "OLD_EVENT", Deprecated = true });
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("OLD_EVENT", d.Message);
        Assert.Contains("deprecated", d.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_deprecated_event_emits_no_diagnostics()
    {
        var fact = new StoryEventFact("file:///test.xml", 1, 0, 0, "ACTIVE_EVENT", false,
            new EnumValueDefinition { Name = "ACTIVE_EVENT", Deprecated = false });
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_event_type_with_null_def_emits_no_diagnostics()
    {
        var fact = new StoryEventFact("file:///test.xml", 1, 0, 0, "UNKNOWN", false, null);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Deprecated_reward_emits_warning()
    {
        var fact = new StoryEventFact("file:///test.xml", 1, 0, 0, "OLD_REWARD", true,
            new EnumValueDefinition { Name = "OLD_REWARD", Deprecated = true });
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Single(results);
    }
}