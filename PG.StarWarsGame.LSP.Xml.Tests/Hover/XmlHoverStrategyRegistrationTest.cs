// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Xml.HoverStrategies;

namespace PG.StarWarsGame.LSP.Xml.Tests.Hover;

public sealed class XmlHoverStrategyRegistrationTest
{
    private static IReadOnlyCollection<Type> AllConcreteStrategyTypes()
    {
        return typeof(XmlLanguageServiceExtensions).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IXmlHoverStrategy).IsAssignableFrom(t))
            .ToList();
    }

    private static IReadOnlyCollection<Type> RegisteredStrategyTypes()
    {
        var services = new ServiceCollection();
        services.AddXmlLanguageServices();
        return services
            .Where(d => d.ServiceType == typeof(IXmlHoverStrategy))
            .Select(d => d.ImplementationType)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
    }

    [Fact]
    public void Every_concrete_strategy_is_registered()
    {
        var expected = AllConcreteStrategyTypes().ToHashSet();
        var registered = RegisteredStrategyTypes().ToHashSet();

        var missing = expected.Except(registered).ToList();
        var extra = registered.Except(expected).ToList();

        Assert.True(missing.Count == 0,
            "Concrete strategies not registered in AddXmlLanguageServices: " +
            string.Join(", ", missing.Select(t => t.Name)));
        Assert.True(extra.Count == 0,
            "Registered strategy types that do not exist as concrete strategies: " +
            string.Join(", ", extra.Select(t => t.Name)));
    }

    [Fact]
    public void Registered_strategy_count_is_locked()
    {
        const int expectedCount = 3;
        Assert.Equal(expectedCount, RegisteredStrategyTypes().Count);
    }

    [Fact]
    public void Each_strategy_is_registered_exactly_once()
    {
        var duplicates = RegisteredStrategyTypes()
            .GroupBy(t => t)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Name)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Strategies registered more than once: " + string.Join(", ", duplicates));
    }
}
