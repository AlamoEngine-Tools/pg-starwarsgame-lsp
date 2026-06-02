// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Tests;

/// <summary>
///     Guards that every concrete <see cref="IXmlDiagnosticsHandler" /> in the Xml assembly is
///     registered exactly once in <c>AddXmlLanguageServices</c>, so additions and deletions
///     cannot silently drift apart from the DI list.
/// </summary>
public sealed class XmlDiagnosticsHandlerRegistrationTest
{
    private static IReadOnlyCollection<Type> AllConcreteHandlerTypes()
    {
        return typeof(XmlLanguageServiceExtensions).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IXmlDiagnosticsHandler).IsAssignableFrom(t))
            .ToList();
    }

    private static IReadOnlyCollection<Type> RegisteredHandlerTypes()
    {
        var services = new ServiceCollection();
        services.AddXmlLanguageServices();
        return services
            .Where(d => d.ServiceType == typeof(IXmlDiagnosticsHandler))
            .Select(d => d.ImplementationType)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
    }

    [Fact]
    public void Every_concrete_handler_is_registered()
    {
        var expected = AllConcreteHandlerTypes().ToHashSet();
        var registered = RegisteredHandlerTypes().ToHashSet();

        var missing = expected.Except(registered).ToList();
        var extra = registered.Except(expected).ToList();

        Assert.True(missing.Count == 0,
            "Concrete handlers not registered in AddXmlLanguageServices: " +
            string.Join(", ", missing.Select(t => t.Name)));
        Assert.True(extra.Count == 0,
            "Registered handler types that do not exist as concrete handlers: " +
            string.Join(", ", extra.Select(t => t.Name)));
    }

    [Fact]
    public void Registered_handler_count_is_locked()
    {
        // Authoritative count of concrete IXmlDiagnosticsHandler registrations. Update this
        // deliberately whenever a handler is added or removed so the change is reviewed.
        const int expectedHandlerCount = 78;

        Assert.Equal(expectedHandlerCount, RegisteredHandlerTypes().Count);
    }

    [Fact]
    public void Each_handler_is_registered_exactly_once()
    {
        var duplicates = RegisteredHandlerTypes()
            .GroupBy(t => t)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Name)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Handlers registered more than once: " + string.Join(", ", duplicates));
    }
}
