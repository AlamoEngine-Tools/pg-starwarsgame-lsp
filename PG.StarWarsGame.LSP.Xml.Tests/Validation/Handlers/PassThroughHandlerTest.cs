// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     These value types have no validator: the engine treats them as opaque pass-through
///     data. Dispatching such a fact through the full registered handler set must yield zero
///     diagnostics. Guards that there is never a registered handler emitting noise for them.
/// </summary>
public sealed class PassThroughHandlerTest
{
    private static readonly IXmlDiagnosticsHandlerRegistry Registry = BuildRegistry();

    private static IXmlDiagnosticsHandlerRegistry BuildRegistry()
    {
        var services = new ServiceCollection();
        services.AddXmlLanguageServices();
        var provider = services.BuildServiceProvider();
        return new XmlDiagnosticsHandlerRegistry(provider.GetServices<IXmlDiagnosticsHandler>());
    }

    [Theory]
    [InlineData(XmlValueType.Type35, "")]
    [InlineData(XmlValueType.Type35, "some value")]
    [InlineData(XmlValueType.Type36, "some value")]
    [InlineData(XmlValueType.Type37, "some value")]
    [InlineData(XmlValueType.Type38, "some value")]
    [InlineData(XmlValueType.AbilityDefinitionSubObjectList, "some value")]
    [InlineData(XmlValueType.GuiActivatedAbilityDefinitionSubObjectList, "some value")]
    public void Unvalidated_value_types_produce_no_diagnostics(XmlValueType valueType, string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tag", valueType);
        var fact = XmlHandlerTestFixtures.MakeFact(tag, value);

        Assert.Empty(Registry.Dispatch(fact, XmlHandlerTestFixtures.EmptyCtx).ToList());
    }
}
