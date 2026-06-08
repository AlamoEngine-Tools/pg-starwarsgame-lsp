// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class GuiActivatedAbilityDefinitionSubObjectListHandlerTest
{
    private static readonly GuiActivatedAbilityDefinitionSubObjectListHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Unit_Abilities_Data", XmlValueType.GuiActivatedAbilityDefinitionSubObjectList);

    [Theory]
    [InlineData("")]
    [InlineData("some content")]
    [InlineData("   ")]
    public void AnyValue_ReturnsNoDiagnostics(string value)
    {
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void WrongType_ReturnsNoDiagnostics()
    {
        var other = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(other, ""), XmlHandlerTestFixtures.EmptyCtx));
    }
}