// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Xml.CodeActions;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

public sealed class XmlCodeActionProviderRegistrationTest
{
    private static IReadOnlyCollection<Type> AllConcreteProviderTypes()
    {
        return typeof(XmlLanguageServiceExtensions).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IXmlCodeActionProvider).IsAssignableFrom(t))
            .ToList();
    }

    private static IReadOnlyCollection<Type> RegisteredProviderTypes()
    {
        var services = new ServiceCollection();
        services.AddXmlLanguageServices();
        return services
            .Where(d => d.ServiceType == typeof(IXmlCodeActionProvider))
            .Select(d => d.ImplementationType)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
    }

    [Fact]
    public void Every_concrete_provider_is_registered()
    {
        var expected = AllConcreteProviderTypes().ToHashSet();
        var registered = RegisteredProviderTypes().ToHashSet();

        var missing = expected.Except(registered).ToList();
        var extra = registered.Except(expected).ToList();

        Assert.True(missing.Count == 0,
            "Concrete providers not registered in AddXmlLanguageServices: " +
            string.Join(", ", missing.Select(t => t.Name)));
        Assert.True(extra.Count == 0,
            "Registered provider types that do not exist as concrete providers: " +
            string.Join(", ", extra.Select(t => t.Name)));
    }

    [Fact]
    public void Registered_provider_count_is_locked()
    {
        const int expectedProviderCount = 2;
        Assert.Equal(expectedProviderCount, RegisteredProviderTypes().Count);
    }

    [Fact]
    public void Each_provider_is_registered_exactly_once()
    {
        var duplicates = RegisteredProviderTypes()
            .GroupBy(t => t)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Name)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Providers registered more than once: " + string.Join(", ", duplicates));
    }
}
