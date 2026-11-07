// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     What resolving by load order costs on a hot path: every indexed and shipped name resolved by
///     type and by id alone, against the same fresh index with and without its load order. A
///     measurement rig, not a gate - it runs only with <c>AET_PROBE_LOOKUP_COST=1</c> and prints.
/// </summary>
[Trait("Category", "E2E")]
public sealed class LoadOrderLookupCostProbe(ITestOutputHelper output)
{
    private const string ProbeVariable = "AET_PROBE_LOOKUP_COST";

    [Theory]
    [InlineData("foc")]
    [InlineData("eaw")]
    public async Task MeasureLookupCost(string corpus)
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) is not { Length: > 0 })
            throw new Exception($"$XunitDynamicSkip$Set {ProbeVariable}=1 to run this measurement.");
        var root = LspTestEnvironment.RepositoryRoot
                   ?? throw new InvalidOperationException("No solution above the test binaries.");
        var workspace = Path.Combine(root, corpus);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));

        var options = ServerConfigurator.Apply(new LanguageServerOptions());
        options.Services.AddSingleton<ILanguageServerFacade>(_ => null!);
        options.Services.AddSingleton<IServerWorkDoneManager>(_ => null!);
        await using var provider = options.Services.BuildServiceProvider();
        provider.GetRequiredService<ILspConfigurationProvider>().LoadFrom(JsonSerializer.SerializeToElement(new
        {
            workspaceRoot = workspace,
            schemaLocalPath = LspTestEnvironment.SchemaLocalPath,
            gamePath = LspTestEnvironment.GamePath,
            baselineLocalPath = LspTestEnvironment.BaselineLocalPath,
            locale = LspTestEnvironment.Locale
        }));
        await Task.WhenAll(
            provider.GetRequiredService<ISchemaBootstrapper>().LoadAsync(cts.Token),
            provider.GetRequiredService<IBaselineBootstrapper>().LoadAsync(
                provider.GetRequiredService<ITargetGameProbe>().Probe([workspace]), cts.Token));
        await provider.GetRequiredService<IModProjectReloadService>().LoadAsync([workspace], cts.Token);

        var ordered = provider.GetRequiredService<IGameIndexService>().Current;
        var layered = ordered with { LoadOrder = LoadOrder.Unknown };
        var names = ordered.WorkspaceDefinitions.Values.SelectMany(d => d)
            .Concat(ordered.Baseline.Symbols.Values)
            .Select(s => (s.Id, s.TypeName ?? string.Empty))
            .Distinct()
            .ToList();

        var visible = Stopwatch.StartNew();
        var shipped = ordered.ShippedSymbols.Count;
        visible.Stop();
        output.WriteLine(
            $"{corpus}: {names.Count} names, {ordered.Baseline.Symbols.Count} shipped, {shipped} loaded; " +
            $"visible set {visible.Elapsed.TotalMilliseconds:F1} ms");
        output.WriteLine($"{corpus}: by load order {Time(ordered, names):F0} ns per name, " +
                         $"by layer {Time(layered, names):F0} ns per name");
    }

    // Best of five rounds, each resolving every name by type and by id.
    private static double Time(GameIndex index, List<(string Id, string Type)> names)
    {
        var best = double.MaxValue;
        for (var round = 0; round < 5; round++)
        {
            var watch = Stopwatch.StartNew();
            foreach (var (id, type) in names)
            {
                index.ResolveOfType(id, type);
                index.Resolve(id);
            }

            watch.Stop();
            best = Math.Min(best, watch.Elapsed.TotalNanoseconds / names.Count);
        }

        return best;
    }
}