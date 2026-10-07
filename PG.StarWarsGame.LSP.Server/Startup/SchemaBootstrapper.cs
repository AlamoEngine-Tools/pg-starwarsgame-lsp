// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Schema;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Schema.Versioning;
using PG.StarWarsGame.LSP.Server.Status;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     First pipeline stage: selects the local or HTTP schema source from the loaded configuration,
///     configures the late-binding <see cref="SchemaProviderProxy" />, and - crucially - awaits the
///     load to completion before returning, so indexing never starts against an empty schema. The
///     Lua API schema is loaded the same way. Treated as static for the session: no hot-reload.
/// </summary>
public sealed class SchemaBootstrapper : ISchemaBootstrapper
{
    private readonly SchemaHttpCache _cache;
    private readonly ILspConfigurationProvider _config;
    private readonly IFileHelper _fileHelper;
    private readonly IFileSystem _fileSystem;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpSchemaProvider> _httpLogger;
    private readonly ILogger<LocalFileSchemaProvider> _localLogger;
    private readonly SchemaLocationResolver _locations;
    private readonly ILogger<SchemaBootstrapper> _logger;
    private readonly LuaApiSchemaProxy _luaProxy;
    private readonly IUserNotifier _notifier;
    private readonly SchemaProviderProxy _proxy;
    private readonly ServerStatusRecorder? _status;
    private readonly LuaStubLocation? _luaStubs;

    public SchemaBootstrapper(
        ILspConfigurationProvider config,
        SchemaProviderProxy proxy,
        LuaApiSchemaProxy luaProxy,
        IFileSystem fileSystem,
        IFileHelper fileHelper,
        IHttpClientFactory httpClientFactory,
        SchemaHttpCache cache,
        SchemaLocationResolver locations,
        IUserNotifier notifier,
        ILogger<SchemaBootstrapper> logger,
        ILogger<LocalFileSchemaProvider> localLogger,
        ILogger<HttpSchemaProvider> httpLogger,
        ServerStatusRecorder? status = null,
        // Optional: the Lua analyzer reads the stubs from the directory recorded here.
        LuaStubLocation? luaStubs = null)
    {
        _status = status;
        _luaStubs = luaStubs;
        _config = config;
        _proxy = proxy;
        _luaProxy = luaProxy;
        _fileSystem = fileSystem;
        _fileHelper = fileHelper;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _locations = locations;
        _notifier = notifier;
        _logger = logger;
        _localLogger = localLogger;
        _httpLogger = httpLogger;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        _logger.LogInformation("Loading schema started.");
        var src = _config.Current.SchemaSource;
        var isLocal = src.Type == SchemaSourceType.Local && !string.IsNullOrWhiteSpace(src.LocalPath);

        ISchemaProvider realProvider;
        SchemaLocation? location = null;
        if (isLocal)
        {
            realProvider = new LocalFileSchemaProvider(src.LocalPath!, _fileSystem, _localLogger);
        }
        else
        {
            location = await _locations.ResolveAsync(src.Repository, src.Url, ct);
            _logger.LogInformation("Schema source: {Origin} {Tag} at {BaseUrl}",
                location.Origin, location.Tag ?? "-", location.BaseUrl);
            realProvider = new HttpSchemaProvider(
                _httpClientFactory.CreateClient(nameof(HttpSchemaProvider)), location.BaseUrl, _cache, _httpLogger,
                location.Tag);
        }

        _proxy.Configure(realProvider);

        if (location is null)
            // LocalFileSchemaProvider loads synchronously in its constructor.
            LoadLuaSchemaFromDisk(src.LocalPath!);
        else
            // Both HTTP downloads are independent - fan them out in parallel. The Lua API ships in
            // the same repository, so it is read from the same release or branch.
            await Task.WhenAll(
                LoadEaWSchemaAsync((HttpSchemaProvider)realProvider, ct),
                LoadLuaSchemaFromHttpAsync(DeriveLuaHttpUrl(location.BaseUrl), ct));

        ReportVersionIncompatibility(realProvider);

        // Recorded here because this is the last place the real provider is reachable: the proxy
        // hides it, and its version check goes with it.
        _status?.RecordSchema(
            StatusSource(location),
            (realProvider as IVersionedSchemaProvider)?.LastVersionCheck,
            (realProvider as HttpSchemaProvider)?.LastLoadFromCache);

        _logger.LogInformation("Loading schema completed.");
    }

    private static StatusSchemaSource StatusSource(SchemaLocation? location)
    {
        return location?.Origin switch
        {
            null => StatusSchemaSource.Local,
            SchemaOrigin.Release => StatusSchemaSource.Release,
            SchemaOrigin.CachedRelease => StatusSchemaSource.CachedRelease,
            SchemaOrigin.Branch => StatusSchemaSource.Branch,
            _ => StatusSchemaSource.CustomUrl
        };
    }

    /// <summary>
    ///     Surfaces a refused schema to the user. Nothing works in that state - no hover,
    ///     completion, navigation or validation - and a silent, wholly inert extension reads as a
    ///     bug rather than as "your schema is newer than your extension", so the log alone is not
    ///     enough. Only a refusal is reported: a merely older or unversioned schema still works.
    /// </summary>
    private void ReportVersionIncompatibility(ISchemaProvider provider)
    {
        if (provider is not IVersionedSchemaProvider { LastVersionCheck: { CanLoad: false } check })
            return;

        _logger.LogError("Schema version check failed: {Message}", check.Message);
        _notifier.ShowError(check.Message);
    }

    private async Task LoadEaWSchemaAsync(HttpSchemaProvider provider, CancellationToken ct)
    {
        try
        {
            await provider.LoadAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP schema load failed; proceeding with whatever was cached.");
        }
    }

    // ── the Lua stub files ───────────────────────────────────────────────────
    // The schema repository's lua/ directory holds several ---@meta files - the engine API, the
    // standard library it opens, the per-script globals - listed in lua/_files.json in load order.
    // A schema release from before that manifest existed has api.d.lua alone, so a missing
    // manifest means exactly that list.

    private const string LuaManifestName = "_files.json";
    private const string LuaApiFileName = "api.d.lua";
    private static readonly JsonSerializerOptions LuaManifestJsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal void LoadLuaSchemaFromDisk(string eawLocalPath)
    {
        var luaDir = DeriveLuaLocalDirectory(eawLocalPath);
        var fs = _fileHelper.FileSystem;
        var manifestPath = Path.Combine(luaDir, LuaManifestName);
        var files = fs.File.Exists(manifestPath)
            ? ParseLuaManifest(fs.File.ReadAllText(manifestPath))
            : [LuaApiFileName];

        var contents = new List<string>();
        foreach (var file in files)
        {
            var path = Path.Combine(luaDir, file);
            if (fs.File.Exists(path))
                contents.Add(fs.File.ReadAllText(path));
            else
                _logger.LogWarning("Lua stub file not found at {Path}", path);
        }

        _luaProxy.Configure(new LuaApiSchemaProvider(contents));
        if (_luaStubs is not null) _luaStubs.Directory = luaDir;
        _logger.LogInformation("Lua schema loaded from {Dir}: {Count} of {Listed} files", luaDir, contents.Count,
            files.Count);
    }

    private async Task LoadLuaSchemaFromHttpAsync(string luaBaseUrl, CancellationToken ct)
    {
        _logger.LogInformation("Loading Lua schema from {Url}", luaBaseUrl);
        var http = _httpClientFactory.CreateClient("LuaSchema");

        IReadOnlyList<string> files;
        try
        {
            files = ParseLuaManifest(await http.GetStringAsync(luaBaseUrl + LuaManifestName, ct));
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "No Lua stub manifest at {Url}; reading {File} alone", luaBaseUrl,
                LuaApiFileName);
            files = [LuaApiFileName];
        }

        // Apply whatever is cached immediately so the parser has a schema while downloading.
        var cached = new List<string>();
        foreach (var file in files)
            if (_cache.TryLoadText("lua/" + file, out var existing))
                cached.Add(existing);
        if (cached.Count > 0)
            _luaProxy.Configure(new LuaApiSchemaProvider(cached));

        var fresh = await Task.WhenAll(files.Select(async file =>
        {
            try
            {
                var content = await http.GetStringAsync(luaBaseUrl + file, ct);
                _cache.UpdateText("lua/" + file, content);
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download Lua stub file {Url}", luaBaseUrl + file);
                return null;
            }
        }));

        var loaded = fresh.Where(c => c is not null).Select(c => c!).ToList();
        if (loaded.Count == files.Count)
        {
            _luaProxy.Configure(new LuaApiSchemaProvider(loaded));
            _logger.LogInformation("Lua schema loaded from {Url}: {Count} files", luaBaseUrl, loaded.Count);
        }
        else if (cached.Count > 0)
        {
            _logger.LogWarning("Lua schema download incomplete; using the cached version");
        }
        else if (loaded.Count > 0)
        {
            _luaProxy.Configure(new LuaApiSchemaProvider(loaded));
            _logger.LogWarning("Lua schema download incomplete; {Count} of {Listed} files loaded", loaded.Count,
                files.Count);
        }
        else
        {
            _logger.LogWarning("Failed to download the Lua schema from {Url}; Lua XML references will not be validated",
                luaBaseUrl);
        }

        // Downloaded stubs are kept in the cache, which is where the analyzer reads them from.
        if (_luaStubs is not null && (cached.Count > 0 || loaded.Count > 0)) _luaStubs.Directory = _cache.PathOf("lua");
        _logger.LogInformation("Loading Lua schema completed.");
    }

    private static IReadOnlyList<string> ParseLuaManifest(string json)
    {
        var manifest = JsonSerializer.Deserialize<LuaStubManifest>(json, LuaManifestJsonOptions);
        return manifest?.Files is { Count: > 0 } files ? files : [LuaApiFileName];
    }

    private static string DeriveLuaLocalDirectory(string eawLocalPath)
    {
        var trimmed = eawLocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed) ?? trimmed;
        return Path.Combine(parent, "lua");
    }

    private static string DeriveLuaHttpUrl(string eawUrl)
    {
        // Treat eawUrl as a base URI and resolve "../lua/" relative to it.
        // e.g. "https://host/main/eaw/" -> "https://host/main/lua/"
        var baseUri = new Uri(eawUrl.EndsWith('/') ? eawUrl : eawUrl + '/');
        return new Uri(baseUri, "../lua/").ToString();
    }

    private sealed record LuaStubManifest(List<string>? Files);
}