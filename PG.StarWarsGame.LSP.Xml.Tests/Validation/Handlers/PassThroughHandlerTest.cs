// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     Structural container types whose handlers are intentional no-ops: they hold sub-object
///     children rather than a scalar value, so dispatching any value through the full registered
///     handler set must yield zero diagnostics.
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
    [InlineData(XmlValueType.AbilityDefinitionSubObjectList, "some content")]
    [InlineData(XmlValueType.AbilityDefinitionSubObjectList, "")]
    [InlineData(XmlValueType.GuiActivatedAbilityDefinitionSubObjectList, "some content")]
    [InlineData(XmlValueType.GuiActivatedAbilityDefinitionSubObjectList, "")]
    public void StructuralContainerTypes_AlwaysProduceNoDiagnostics(XmlValueType valueType, string value)
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Tag", valueType);
        var fact = XmlHandlerTestFixtures.MakeFact(tag, value);

        Assert.Empty(Registry.Dispatch(fact, XmlHandlerTestFixtures.EmptyCtx).ToList());
    }
}