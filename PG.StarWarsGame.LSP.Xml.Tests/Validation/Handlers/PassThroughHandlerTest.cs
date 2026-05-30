// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class PassThroughHandlerTest
{
    [Theory]
    [InlineData("")]
    [InlineData("some value")]
    [InlineData("  ")]
    public void Type35_always_returns_no_diagnostics(string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tag", XmlValueType.Type35);
        var results = new Type35Handler().Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("some value")]
    public void Type36_always_returns_no_diagnostics(string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tag", XmlValueType.Type36);
        var results = new Type36Handler().Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("some value")]
    public void Type37_always_returns_no_diagnostics(string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tag", XmlValueType.Type37);
        var results = new Type37Handler().Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("some value")]
    public void Type38_always_returns_no_diagnostics(string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tag", XmlValueType.Type38);
        var results = new Type38Handler().Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("some value")]
    public void Type56_always_returns_no_diagnostics(string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tag", XmlValueType.Type56);
        var results = new Type56Handler().Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("some value")]
    public void Type57_always_returns_no_diagnostics(string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tag", XmlValueType.Type57);
        var results = new Type57Handler().Handle(XmlHandlerTestFixtures.MakeFact(tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }
}
