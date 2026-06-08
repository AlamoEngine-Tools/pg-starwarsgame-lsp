// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     First pipeline stage: selects the local or HTTP schema source from the loaded configuration,
///     configures the late-binding <see cref="SchemaProviderProxy" />, and — crucially — awaits the
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
    private readonly ILogger<SchemaBootstrapper> _logger;
    private readonly LuaApiSchemaProxy _luaProxy;
    private readonly SchemaProviderProxy _proxy;

    public SchemaBootstrapper(
        ILspConfigurationProvider config,
        SchemaProviderProxy proxy,
        LuaApiSchemaProxy luaProxy,
        IFileSystem fileSystem,
        IFileHelper fileHelper,
        IHttpClientFactory httpClientFactory,
        SchemaHttpCache cache,
        ILogger<SchemaBootstrapper> logger,
        ILogger<LocalFileSchemaProvider> localLogger,
        ILogger<HttpSchemaProvider> httpLogger)
    {
        _config = config;
        _proxy = proxy;
        _luaProxy = luaProxy;
        _fileSystem = fileSystem;
        _fileHelper = fileHelper;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
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
        if (isLocal)
            realProvider = new LocalFileSchemaProvider(src.LocalPath, _fileSystem, _localLogger);
        else
            realProvider = new HttpSchemaProvider(
                _httpClientFactory.CreateClient(nameof(HttpSchemaProvider)), src.Url, _cache, _httpLogger);

        _proxy.Configure(realProvider);

        if (isLocal)
            // LocalFileSchemaProvider loads synchronously in its constructor.
            LoadLuaSchemaFromDisk(src.LocalPath!);
        else
            // Both HTTP downloads are independent — fan them out in parallel.
            await Task.WhenAll(
                LoadEaWSchemaAsync((HttpSchemaProvider)realProvider, ct),
                LoadLuaSchemaFromHttpAsync(DeriveLuaHttpUrl(src.Url), ct));

        _logger.LogInformation("Loading schema completed.");
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

    private void LoadLuaSchemaFromDisk(string eawLocalPath)
    {
        var luaSchemaPath = DeriveLuaLocalPath(eawLocalPath);
        if (_fileHelper.FileSystem.File.Exists(luaSchemaPath))
        {
            _luaProxy.Configure(new LuaApiSchemaProvider([_fileHelper.FileSystem.File.ReadAllText(luaSchemaPath)]));
            _logger.LogInformation("Lua schema loaded from {Path}", luaSchemaPath);
        }
        else
        {
            _logger.LogWarning("Lua schema not found at {Path}", luaSchemaPath);
        }
    }

    private async Task LoadLuaSchemaFromHttpAsync(string luaUrl, CancellationToken ct)
    {
        _logger.LogInformation("Loading Lua schema from {Url}", luaUrl);
        const string cacheKey = "lua/api.d.lua";
        var http = _httpClientFactory.CreateClient("LuaSchema");

        // Apply any cached version immediately so the parser has a schema while downloading.
        string? cached = null;
        if (_cache.TryLoadText(cacheKey, out var existing))
        {
            cached = existing;
            _luaProxy.Configure(new LuaApiSchemaProvider([existing]));
        }

        try
        {
            var fresh = await http.GetStringAsync(luaUrl, ct);
            _cache.UpdateText(cacheKey, fresh);
            _luaProxy.Configure(new LuaApiSchemaProvider([fresh]));
            _logger.LogInformation("Lua schema loaded from {Url}", luaUrl);
        }
        catch (Exception ex)
        {
            if (cached is { Length: > 0 })
                _logger.LogWarning(ex, "Failed to download Lua schema from {Url}; using cached version", luaUrl);
            else
                _logger.LogWarning(ex,
                    "Failed to download Lua schema from {Url}; Lua XML references will not be validated", luaUrl);
        }

        _logger.LogInformation("Loading Lua schema completed.");
    }

    private static string DeriveLuaLocalPath(string eawLocalPath)
    {
        var trimmed = eawLocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed) ?? trimmed;
        return Path.Combine(parent, "lua", "api.d.lua");
    }

    private static string DeriveLuaHttpUrl(string eawUrl)
    {
        // Treat eawUrl as a base URI and resolve "../lua/api.d.lua" relative to it.
        // e.g. "https://host/main/eaw/" → "https://host/main/lua/api.d.lua"
        var baseUri = new Uri(eawUrl.EndsWith('/') ? eawUrl : eawUrl + '/');
        return new Uri(baseUri, "../lua/api.d.lua").ToString();
    }
}