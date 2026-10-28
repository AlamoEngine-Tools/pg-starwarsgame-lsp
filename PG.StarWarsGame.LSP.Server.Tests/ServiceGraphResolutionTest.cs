// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Lua.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Server.Preview;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Story.Dialog;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     Every service the server registers can be built from the server's own container, and a
///     missing registration fails there - by name - instead of constructing a service with a hole in
///     it.
/// </summary>
/// <remarks>
///     <para>
///         The container was free to skip an optional constructor parameter (<c>IFoo? foo = null</c>)
///         when nothing registered <c>IFoo</c>, and did so silently: the feature behind it switched
///         itself off, or the class built a private fallback. Two production bugs of exactly this kind
///         are recorded in the code's own comments (#305). With every dependency required, the
///         first unresolvable one fails this test with the service's name.
///     </para>
///     <para>
///         The two services the LSP host supplies at runtime stand in as placeholders: nothing under
///         test calls them, and without them most of the graph is unreachable.
///     </para>
/// </remarks>
public sealed class ServiceGraphResolutionTest
{
    private static readonly Type[] HostSupplied = [typeof(ILanguageServerFacade), typeof(IServerWorkDoneManager)];

    private static LanguageServerOptions Configured()
    {
        var options = new LanguageServerOptions();
        ServerConfigurator.Apply(options);
        return options;
    }

    private static ServiceProvider Build(LanguageServerOptions options)
    {
        options.Services.AddSingleton<ILanguageServerFacade>(_ => null!);
        options.Services.AddSingleton<IServerWorkDoneManager>(_ => null!);
        return options.Services.BuildServiceProvider();
    }

    private static ServiceProvider BuildWithout<TService>(out LanguageServerOptions options)
    {
        options = Configured();
        var registered = options.Services.Where(d => d.ServiceType == typeof(TService)).ToList();
        Assert.NotEmpty(registered);
        foreach (var descriptor in registered) options.Services.Remove(descriptor);
        return Build(options);
    }

    [Fact]
    public void EveryRegisteredServiceResolves()
    {
        var options = Configured();
        using var provider = Build(options);

        var failures = new List<string>();
        foreach (var serviceType in options.Services
                     .Select(d => d.ServiceType)
                     .Where(t => !t.IsGenericTypeDefinition && !HostSupplied.Contains(t))
                     .Distinct())
            try
            {
                _ = provider.GetServices(serviceType).ToList();
            }
            catch (Exception ex)
            {
                failures.Add($"{serviceType.Name}: {ex.GetBaseException().Message}");
            }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    ///     Handlers go through <c>WithHandler</c> into the LSP host's own container, so they are not
    ///     resolvable here; constructing them the way the host does - every parameter from the
    ///     container - is the same check.
    /// </summary>
    [Fact]
    public void EveryHandlerConstructs()
    {
        var options = Configured();
        using var provider = Build(options);

        var handlerTypes = HandlerTypesOf(options);
        Assert.NotEmpty(handlerTypes);

        var failures = new List<string>();
        foreach (var handlerType in handlerTypes)
            try
            {
                _ = ActivatorUtilities.CreateInstance(provider, handlerType);
            }
            catch (Exception ex)
            {
                failures.Add($"{handlerType.Name}: {ex.GetBaseException().Message}");
            }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    ///     The types behind every <c>WithHandler</c>. OmniSharp keeps them in a handler collection of
    ///     its own on the options, not in the service collection; found by its element type so the
    ///     member's name is not a dependency of this test.
    /// </summary>
    private static List<Type> HandlerTypesOf(LanguageServerOptions options)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var descriptions = new List<JsonRpcHandlerDescription>();
        for (var type = options.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetProperties(any | BindingFlags.DeclaredOnly))
                if (property.GetIndexParameters().Length == 0 &&
                    property.GetValue(options) is IEnumerable<JsonRpcHandlerDescription> held)
                    descriptions.AddRange(held);
            foreach (var field in type.GetFields(any | BindingFlags.DeclaredOnly))
                if (field.GetValue(options) is IEnumerable<JsonRpcHandlerDescription> held)
                    descriptions.AddRange(held);
        }

        return descriptions.OfType<JsonRpcHandlerTypeDescription>().Select(d => d.HandlerType).Distinct().ToList();
    }

    // ── one registration removed: the service names it instead of constructing without it ────

    [Fact]
    public void XmlDiagnosticsPublisher_WithoutTheIconNameIndex_NamesIt()
    {
        using var provider = BuildWithout<IIconNameIndex>(out _);

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<XmlDiagnosticsPublisher>());

        Assert.Contains(nameof(IIconNameIndex), ex.Message);
    }

    [Fact]
    public void LuaDiagnosticsPublisher_WithoutTheAnalyzer_NamesIt()
    {
        using var provider = BuildWithout<ILuaAnalyzer>(out _);

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<LuaDiagnosticsPublisher>());

        Assert.Contains(nameof(ILuaAnalyzer), ex.Message);
    }

    [Fact]
    public void DialogDiagnosticsPublisher_WithoutTheSuppressionStore_NamesIt()
    {
        using var provider = BuildWithout<IGlobalSuppressionStore>(out _);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<DialogDiagnosticsPublisher>());

        Assert.Contains(nameof(IGlobalSuppressionStore), ex.Message);
    }

    [Fact]
    public void WorkspaceIndexer_WithoutTheLuaSchema_NamesIt()
    {
        using var provider = BuildWithout<ILuaApiSchemaProvider>(out _);

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<WorkspaceIndexer>());

        Assert.Contains(nameof(ILuaApiSchemaProvider), ex.Message);
    }

    [Fact]
    public void ModProjectReloadService_WithoutTheRefreshNotifier_NamesIt()
    {
        using var provider = BuildWithout<IClientRefreshNotifier>(out _);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IModProjectReloadService>());

        Assert.Contains(nameof(IClientRefreshNotifier), ex.Message);
    }

    /// <summary>
    ///     One constructor each: a second, shorter one is what the container picked when the long
    ///     one had an unresolvable parameter, and every test built on it kept passing.
    /// </summary>
    [Theory]
    [InlineData(typeof(XmlDiagnosticsPublisher))]
    [InlineData(typeof(LuaDiagnosticsPublisher))]
    [InlineData(typeof(DialogDiagnosticsPublisher))]
    [InlineData(typeof(ParticleFileDiagnosticsPublisher))]
    [InlineData(typeof(PreviewSceneBuilder))]
    [InlineData(typeof(WorkspaceIndexer))]
    [InlineData(typeof(ModProjectLoader))]
    public void TheClassesTheReviewNamed_HaveOneConstructor(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        var constructor = Assert.Single(constructors);
        Assert.All(constructor.GetParameters(), p => Assert.False(p.HasDefaultValue,
            $"{type.Name}({p.ParameterType.Name} {p.Name}) has a default - a dependency the container may skip"));
    }
}