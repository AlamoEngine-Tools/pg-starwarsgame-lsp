// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests;

/// <summary>
///     Guards that every concrete <see cref="IXmlCrossTagRule" /> in the Xml assembly is
///     registered exactly once in <c>AddXmlLanguageServices</c>.
/// </summary>
public sealed class XmlCrossTagRuleRegistrationTest
{
    private static IReadOnlyCollection<Type> AllConcreteRuleTypes()
    {
        return typeof(XmlLanguageServiceExtensions).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IXmlCrossTagRule).IsAssignableFrom(t))
            .ToList();
    }

    private static IReadOnlyCollection<Type> RegisteredRuleTypes()
    {
        var services = new ServiceCollection();
        services.AddXmlLanguageServices();
        return services
            .Where(d => d.ServiceType == typeof(IXmlCrossTagRule))
            .Select(d => d.ImplementationType)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
    }

    [Fact]
    public void Every_concrete_rule_is_registered()
    {
        var expected = AllConcreteRuleTypes().ToHashSet();
        var registered = RegisteredRuleTypes().ToHashSet();

        var missing = expected.Except(registered).ToList();
        var extra = registered.Except(expected).ToList();

        Assert.True(missing.Count == 0,
            "Concrete IXmlCrossTagRule types not registered in AddXmlLanguageServices: " +
            string.Join(", ", missing.Select(t => t.Name)));
        Assert.True(extra.Count == 0,
            "Registered IXmlCrossTagRule types that do not exist as concrete implementations: " +
            string.Join(", ", extra.Select(t => t.Name)));
    }

    [Fact]
    public void Each_rule_is_registered_exactly_once()
    {
        var duplicates = RegisteredRuleTypes()
            .GroupBy(t => t)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Name)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "IXmlCrossTagRule types registered more than once: " + string.Join(", ", duplicates));
    }
}