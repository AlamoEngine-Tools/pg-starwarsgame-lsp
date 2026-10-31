// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     What indexing the FoC corpus produces, pinned (#315): symbol counts per type, the file-type
///     registry, the asset and model catalogs and the dynamic enums, compared against
///     <c>Golden/index/foc.json</c>. The workspace indexer's split into a metafile registrar, a
///     document indexer and the catalog builders must leave the recording identical.
/// </summary>
/// <remarks>
///     <para>
///         Runs the server's own container in process rather than over the wire: none of these counts
///         is on the protocol, and adding an endpoint for a test would be a protocol method with no
///         client caller. The two services the LSP host supplies stand in as placeholders, as in
///         <c>ServiceGraphResolutionTest</c>; nothing on the index path calls them.
///     </para>
///     <para>
///         The long lists - every registered file, every symbol, every reference - are pinned by a
///         SHA-256 over their sorted lines, with the workspace path scrubbed out, so the recording
///         stays readable while any difference in them still fails. Re-record with
///         <c>AET_GOLDEN_RECORD=1</c> when the schema, the baseline or the corpus changes on purpose.
///     </para>
/// </remarks>
[Trait("Category", "E2E")]
public sealed class WorkspaceIndexGoldenSmokeTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Fact]
    public async Task TheFocCorpus_IndexesTheSame()
    {
        if (LspTestEnvironment.WorkspacePath is not { } workspace || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");

        var root = LspTestEnvironment.RepositoryRoot
                   ?? throw new InvalidOperationException("No solution above the test binaries.");
        var goldenPath = Path.Combine(root, "PG.StarWarsGame.LSP.E2E.Tests", "Golden", "index", "foc.json");
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

        var actual = Scrub(JsonSerializer.Serialize(Record(
            provider.GetRequiredService<IGameIndexService>().Current,
            provider.GetRequiredService<IFileTypeRegistry>(),
            provider.GetRequiredService<IStoryChainProblemStore>(), workspace), JsonOptions), workspace);

        if (Environment.GetEnvironmentVariable(PreviewSceneGoldenSmokeTest.RecordVariable) is { Length: > 0 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            await File.WriteAllTextAsync(goldenPath, actual + "\n", cts.Token);
            return;
        }

        Assert.True(File.Exists(goldenPath),
            $"No recording at {goldenPath}; record one with {PreviewSceneGoldenSmokeTest.RecordVariable}=1");
        var expected = (await File.ReadAllTextAsync(goldenPath, cts.Token)).TrimEnd('\n', '\r');
        Assert.Equal(expected, actual);
    }

    private static object Record(GameIndex index, IFileTypeRegistry registry, IStoryChainProblemStore storyProblems,
        string workspace)
    {
        var symbols = index.WorkspaceDefinitions.Values.SelectMany(s => s).ToList();
        var registered = registry.All;
        var storyProblemCount = index.Documents.Keys.Sum(uri => storyProblems.GetForDocument(uri).Count);

        return new
        {
            documents = index.Documents.Count,
            symbols = new
            {
                total = symbols.Count,
                byType = symbols.GroupBy(s => s.TypeName ?? "<none>", StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count()),
                sha256 = Hash(symbols.Select(s => $"{s.TypeName}|{s.Id}|{Where(s.Origin)}"), workspace)
            },
            references = new
            {
                total = index.WorkspaceReferences.Values.Sum(r => r.Length),
                // Not keyed by the dictionary's key: it is folded, and which spelling it keeps is
                // whichever reference the parallel scan reached first.
                sha256 = Hash(index.WorkspaceReferences.Values.SelectMany(r => r).Select(r => r.ToString()), workspace)
            },
            fileTypeRegistry = new
            {
                total = registered.Count,
                byType = registered.Values.SelectMany(t => t.IsEmpty ? ["<untyped>"] : t.AsEnumerable())
                    .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count()),
                sha256 = Hash(registered.Select(kv => $"{kv.Key}|{string.Join(',', kv.Value)}"), workspace)
            },
            assets = MegAssetCatalogBuilder.KnownAssetExtensions.Order(StringComparer.Ordinal)
                .ToDictionary(ext => ext, ext => index.AssetFiles.GetByExtension(ext).Count()),
            models = new
            {
                bones = index.ModelBones.Count,
                boneNames = index.ModelBones.Values.Sum(b => b.Length),
                textures = index.ModelTextures.Count,
                particleSystems = index.ParticleSystems.Count,
                proxies = index.ModelProxies.Count,
                sha256 = Hash(index.ModelBones.Select(kv => $"{kv.Key}|{string.Join(',', kv.Value)}")
                    .Concat(index.ModelTextures.Select(kv => $"t|{kv.Key}|{string.Join(',', kv.Value)}"))
                    .Concat(index.ParticleSystems.Select(p => $"p|{p}"))
                    .Concat(index.ModelProxies.Select(kv => $"x|{kv.Key}|{string.Join(',', kv.Value)}")), workspace)
            },
            dynamicEnums = index.WorkspaceDynamicEnumValues.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Length),
            dynamicEnumDefinitions = index.WorkspaceEnumValueDefinitions.Values.Sum(d => d.Count),
            storyChainProblems = storyProblemCount
        };
    }

    private static string Where(SymbolOrigin origin)
    {
        return origin is FileOrigin file ? $"{file.Uri}:{file.Line}" : origin.ToString();
    }

    /// <summary>SHA-256 over the sorted, scrubbed lines - order-free, machine-free.</summary>
    private static string Hash(IEnumerable<string> lines, string workspace)
    {
        var sorted = lines.Select(l => Scrub(l, workspace)).Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', sorted))));
    }

    /// <summary>The workspace path in every spelling the index writes becomes <c>&lt;ws&gt;</c>.</summary>
    private static string Scrub(string text, string workspace)
    {
        var forward = workspace.Replace('\\', '/');
        foreach (var spelling in new[]
                 {
                     "file:///" + Uri.EscapeDataString(forward).Replace("%2F", "/"), "file:///" + forward,
                     workspace, forward, workspace.Replace("\\", "\\\\")
                 })
            text = text.Replace(spelling, "<ws>", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
