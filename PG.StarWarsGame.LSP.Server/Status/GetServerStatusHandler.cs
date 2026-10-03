// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using System.Runtime.InteropServices;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Schema.Versioning;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Status;

// ── aet/getServerStatus ──────────────────────────────────────────────────────
//
// What the server can say about itself for a bug report. Every enum travels as its NAME, written
// out here, because a bare enum on an aet/* DTO goes out as an ordinal and the client reads names.

[Method("aet/getServerStatus", Direction.ClientToServer)]
public sealed record GetServerStatusParams : IRequest<ServerStatusDto>
{
    /// <summary>Also compute the extended tier - counts the author opts in to sharing.</summary>
    public bool Extended { get; init; }
}

/// <param name="Layers">Project layers indexed, the root included.</param>
public sealed record ServerStatusDependenciesDto(int Direct, int Total, int Depth, int Unresolved, int Layers);

/// <param name="ProjectFiles">Indexed documents in the root project's own layer.</param>
/// <param name="DependencyFiles">Indexed documents in every other layer.</param>
public sealed record ServerStatusFilesDto(int ProjectFiles, int DependencyFiles, int XmlFiles, int LuaFiles);

/// <param name="Baseline">Symbols the shipped-game baseline supplies.</param>
public sealed record ServerStatusSymbolsDto(int Xml, int Lua, int Baseline, int LocalisationKeys);

/// <param name="BaseGame">How many of <paramref name="Total" /> come from the shipped game alone.</param>
public sealed record ServerStatusAssetCountDto(string Extension, int Total, int BaseGame);

/// <param name="Schema">Hit, Rebuilt or NotUsed (a local schema has no cache).</param>
public sealed record ServerStatusCachesDto(
    string Schema,
    int SnapshotLayers,
    int RebuiltLayers,
    int FilesReused,
    int FilesParsed,
    int BoneLayersReused,
    int BoneLayers);

public sealed record ServerStatusTypeCountDto(string TypeName, int Count);

/// <summary>The opt-in tier: counts that help rebuild a setup as a test case.</summary>
/// <param name="SymbolTypes">Project symbols by type, most first - too long for an issue body.</param>
public sealed record ServerStatusExtendedDto(
    ServerStatusDependenciesDto Dependencies,
    long? IndexDurationMs,
    ServerStatusFilesDto Files,
    ServerStatusSymbolsDto Symbols,
    IReadOnlyList<ServerStatusAssetCountDto> Assets,
    ServerStatusCachesDto Caches,
    IReadOnlyList<ServerStatusTypeCountDto> SymbolTypes);

/// <param name="Version">The schema's declared version, or null when it declares none.</param>
/// <param name="Compatibility">The version check's verdict, or <c>NotChecked</c>.</param>
/// <param name="Source">Release, CachedRelease, Branch, CustomUrl, Local or NotLoaded.</param>
public sealed record ServerStatusSchemaDto(string? Version, string Compatibility, string Source);

/// <param name="Source">Network, Cache, Local, Empty or NotLoaded.</param>
/// <param name="Official">True when it came from the default URL.</param>
public sealed record ServerStatusBaselineDto(string Source, bool Official, string? BuiltAt, string? ManifestHash);

public sealed record ServerStatusIconPackDto(string Source, bool Official);

/// <param name="ProjectProblem">A <see cref="Project.ProjectProblem" /> name, or null when valid.</param>
public sealed record ServerStatusWorkspaceDto(
    bool ProjectDetected,
    bool ProjectValid,
    string? ProjectProblem,
    int FoldersWithoutProject);

/// <param name="State">Building, Complete or Failed.</param>
public sealed record ServerStatusIndexDto(string State);

/// <summary>Result of <c>aet/getServerStatus</c>: outcomes only, never a path or a name.</summary>
/// <param name="ServerVersion">The full informational version, commit included.</param>
/// <param name="Warnings">Anything empty or failed that should not be, one line each.</param>
public sealed record ServerStatusDto(
    string ServerVersion,
    string Runtime,
    ServerStatusSchemaDto Schema,
    ServerStatusBaselineDto Baseline,
    ServerStatusIconPackDto IconPack,
    ServerStatusWorkspaceDto Workspace,
    ServerStatusIndexDto Index,
    IReadOnlyList<string> Warnings,
    ServerStatusExtendedDto? Extended = null);

/// <summary>Serves <c>aet/getServerStatus</c>.</summary>
public sealed class GetServerStatusHandler(
    ServerStatusRecorder recorder,
    ILspConfigurationProvider config,
    IGameIndexService indexService,
    IModProjectReloadService projects,
    IFileHelper fileHelper,
    IWorkspaceIndexer? indexer = null)
    : IJsonRpcRequestHandler<GetServerStatusParams, ServerStatusDto>
{
    public Task<ServerStatusDto> Handle(GetServerStatusParams request, CancellationToken cancellationToken)
    {
        var index = indexService.Current;
        var check = recorder.SchemaCheck;
        var schema = new ServerStatusSchemaDto(
            check?.DeclaredVersion,
            check?.Compatibility.ToString() ?? "NotChecked",
            recorder.SchemaSource.ToString());

        // The icon pack is the baseline's sidecar, so it is official exactly when the baseline is.
        var official = IsOfficialBaseline(config.Current.BaselineSource);
        var loaded = index.Baseline;
        var hasBuild = loaded.BuiltAt != DateTimeOffset.MinValue;
        var baseline = new ServerStatusBaselineDto(
            recorder.BaselineSource.ToString(),
            official,
            hasBuild ? loaded.BuiltAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ") : null,
            string.IsNullOrEmpty(loaded.SourceManifestHash) ? null : loaded.SourceManifestHash);

        var problem = recorder.ProjectProblem;
        var workspace = new ServerStatusWorkspaceDto(
            recorder.ProjectDetected == true,
            recorder.ProjectDetected == true && problem == ProjectProblem.None,
            problem == ProjectProblem.None ? null : problem.ToString(),
            FoldersWithoutProject());

        var files = Files(index);

        return Task.FromResult(new ServerStatusDto(
            ServerVersion(),
            RuntimeInformation.FrameworkDescription,
            schema,
            baseline,
            new ServerStatusIconPackDto(recorder.IconPackSource.ToString(), official),
            workspace,
            new ServerStatusIndexDto(recorder.IndexState.ToString()),
            Warnings(check, index, problem, files),
            request.Extended ? Extended(index, files) : null));
    }

    // ── extended tier ────────────────────────────────────────────────────────

    private ServerStatusExtendedDto Extended(GameIndex index, ServerStatusFilesDto files)
    {
        var layers = projects.LastWorkspaceConfig?.Layers.Count ?? 0;
        var shape = projects.LastWorkspaceConfig?.Dependencies ?? new ProjectDependencyShape(0, 0, 0, 0);

        return new ServerStatusExtendedDto(
            new ServerStatusDependenciesDto(shape.Direct, shape.Total, shape.Depth, shape.Unresolved, layers),
            recorder.IndexDuration is { } duration ? (long)duration.TotalMilliseconds : null,
            files,
            Symbols(index),
            Assets(index),
            Caches(),
            SymbolTypes(index));
    }

    /// <summary>
    ///     Indexed documents by source and by language. The root project is the leaf layer; every
    ///     other rank is a dependency. Without layers every document is rank 0 and counts as the
    ///     project's own.
    /// </summary>
    private static ServerStatusFilesDto Files(GameIndex index)
    {
        var documents = index.Documents.Values.ToList();
        var leaf = documents.Count == 0 ? 0 : documents.Max(d => d.LayerRank);
        var project = documents.Count(d => d.LayerRank == leaf);

        return new ServerStatusFilesDto(
            project,
            documents.Count - project,
            documents.Count(d => IsLanguage(d, ".xml")),
            documents.Count(d => IsLanguage(d, ".lua")));
    }

    /// <summary>
    ///     Symbols the index holds, by language. Story dialog files are not part of the game index -
    ///     their scope service keeps its own cache - so there is no dialog count to give here.
    /// </summary>
    private static ServerStatusSymbolsDto Symbols(GameIndex index)
    {
        var documents = index.Documents.Values;
        return new ServerStatusSymbolsDto(
            documents.Where(d => IsLanguage(d, ".xml")).Sum(d => d.Symbols.Length),
            documents.Where(d => IsLanguage(d, ".lua")).Sum(d => d.Symbols.Length),
            index.Baseline.Symbols.Count,
            index.Localisation.Keys.Count());
    }

    /// <summary>Every extension the asset catalog records, the empty ones included.</summary>
    private static IReadOnlyList<ServerStatusAssetCountDto> Assets(GameIndex index)
    {
        return MegAssetCatalogBuilder.KnownAssetExtensions
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(extension =>
            {
                var paths = index.AssetFiles.GetByExtension(extension).ToList();
                return new ServerStatusAssetCountDto(extension, paths.Count,
                    paths.Count(index.AssetFiles.IsPackedAsset));
            })
            .ToList();
    }

    private ServerStatusCachesDto Caches()
    {
        var snapshots = indexer?.LastIndexCache;
        var bones = indexer?.LastBoneCatalog;
        var schema = recorder.SchemaFromCache switch
        {
            true => "Hit",
            false => "Rebuilt",
            null => "NotUsed"
        };

        return new ServerStatusCachesDto(
            schema,
            snapshots?.LayersFromSnapshot ?? 0,
            snapshots?.LayersRebuilt ?? 0,
            snapshots?.FilesReused ?? 0,
            snapshots?.FilesParsed ?? 0,
            bones?.LayersReused ?? 0,
            bones?.Layers ?? 0);
    }

    /// <summary>The project's own symbols by type, most first; a symbol with no type by its kind.</summary>
    private static IReadOnlyList<ServerStatusTypeCountDto> SymbolTypes(GameIndex index)
    {
        return index.Documents.Values
            .SelectMany(d => d.Symbols)
            .GroupBy(s => s.TypeName ?? s.Kind.ToString(), StringComparer.Ordinal)
            .Select(g => new ServerStatusTypeCountDto(g.Key, g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.TypeName, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsLanguage(DocumentIndex document, string extension)
    {
        return document.DocumentUri.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The whole informational version. <c>serverInfo</c> strips everything after the <c>+</c>,
    ///     which is the commit - the one part a bug report cannot do without.
    /// </summary>
    private static string ServerVersion()
    {
        return typeof(GetServerStatusHandler).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    }

    private static bool IsOfficialBaseline(BaselineSourceConfig source)
    {
        return source.Type == BaselineSourceType.Http
               && string.Equals(source.Url, new BaselineSourceConfig().Url, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Workspace folders with no <c>.pgproj</c> anywhere below them - searched the way the
    ///     detector searches. The detector stops at the first folder that has one, so in a
    ///     multi-root workspace these are the folders nothing indexes.
    /// </summary>
    private int FoldersWithoutProject()
    {
        var fs = fileHelper.FileSystem;
        return (projects.LastWorkspaceRoots ?? [])
            .Count(root => !fs.Directory.Exists(root)
                           || fs.Directory.GetFiles(root, "*.pgproj", SearchOption.AllDirectories).Length == 0);
    }

    /// <summary>The states that make every other answer suspect, one line each.</summary>
    private IReadOnlyList<string> Warnings(
        SchemaVersionCheck? check, GameIndex index, ProjectProblem problem, ServerStatusFilesDto files)
    {
        var warnings = new List<string>();

        if (check is null)
            warnings.Add("Schema: Not loaded");
        else if (!check.CanLoad)
            warnings.Add($"Schema: {check.Compatibility}");

        if (index.Baseline.Symbols.IsEmpty)
            warnings.Add("Baseline: Empty");

        if (problem != ProjectProblem.None)
            warnings.Add($"Project: {problem}");

        if (recorder.IndexState == StatusIndexState.Failed)
            warnings.Add("Index: Failed");

        // The two that read as "everything is fine" everywhere else: a complete, valid project
        // that indexed nothing, and a catalog with no models in it.
        if (recorder.IndexState == StatusIndexState.Complete && problem == ProjectProblem.None
                                                             && files.ProjectFiles == 0)
            warnings.Add("Project files: 0");

        if (!index.AssetFiles.GetByExtension(".alo").Any())
            warnings.Add("Asset catalog: No models");

        return warnings;
    }
}