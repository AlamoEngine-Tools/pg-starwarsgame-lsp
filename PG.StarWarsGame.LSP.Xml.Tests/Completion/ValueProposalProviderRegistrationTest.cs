// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion;

/// <summary>
///     A value type has ONE proposal provider. The lookup takes the first registered for a type
///     (#312), so a second registration for the same type would be silently ignored - this test
///     fails it loudly instead, as the dictionary the registry built once did at startup.
/// </summary>
public sealed class ValueProposalProviderRegistrationTest
{
    /// <summary>Every provider AddXmlLanguageServices registers, built with the one dependency any of them takes.</summary>
    private static List<IXmlValueProposalProvider> RegisteredProviders()
    {
        var services = new ServiceCollection();
        services.AddXmlLanguageServices();
        var deps = new ServiceCollection()
            .AddSingleton<IGameIndexService>(new FakeGameIndexService())
            .BuildServiceProvider();
        return
        [
            .. services
                .Where(d => d.ServiceType == typeof(IXmlValueProposalProvider))
                .Select(d => d.ImplementationType)
                .OfType<Type>()
                .Select(t => (IXmlValueProposalProvider)ActivatorUtilities.CreateInstance(deps, t))
        ];
    }

    [Fact]
    public void EveryRegisteredProposalProvider_HasAValueTypeOfItsOwn()
    {
        var providers = RegisteredProviders();

        Assert.NotEmpty(providers);
        var shared = providers.GroupBy(p => p.ValueType).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(shared.Count == 0,
            "Value types with more than one proposal provider: " + string.Join(", ", shared));
    }

    [Fact]
    public void TheRegisteredProviders_CoverTheBooleanAndDynamicEnumValueTypes()
    {
        var types = RegisteredProviders().Select(p => p.ValueType).ToHashSet();

        Assert.Contains(XmlValueType.Boolean, types);
        Assert.Contains(XmlValueType.DynamicEnumValue, types);
    }
}