// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Providers;

/// <summary>
///     Loads the schema from a remote HTTP source (e.g. raw GitHub).
///     Fetches _index.json first, then each listed YAML file.
///     Downloaded files are persisted to a local cache; the cache is validated by a SHA-256
///     checksum of the manifest plus all downloaded YAML file contents.
/// </summary>
public sealed class HttpSchemaProvider : ISchemaProvider
{
    private readonly string _baseUrl;
    private readonly SchemaHttpCache _cache;
    private readonly Dictionary<string, string> _etags = new();
    private readonly HttpClient _http;
    private readonly ILogger<HttpSchemaProvider> _logger;

    private readonly Dictionary<string, IReadOnlyList<RawTagDefinition>> _rawTagFallbacks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TaskCompletionSource _readyTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile SchemaIndex _current = SchemaIndex.Empty;
    private IReadOnlyList<RawEnumDefinition> _rawEnumFallbacks = [];

    public HttpSchemaProvider(HttpClient http, string baseUrl, SchemaHttpCache cache,
        ILogger<HttpSchemaProvider> logger)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/') + '/';
        _cache = cache;
        _logger = logger;
    }

    public event EventHandler? SchemaRefreshed;

    /// <inheritdoc />
    public Task ReadyAsync => _readyTcs.Task;

    public XmlTagDefinition? GetTag(string tagName)
    {
        return _current.GetTag(tagName);
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
    {
        return _current.GetAllTagDefinitions(tagName);
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => _current.AllTags;

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return _current.GetObjectType(typeName);
    }

    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => _current.AllObjectTypes;

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return _current.GetTagsForType(typeName);
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return _current.GetEnum(enumName);
    }

    public IReadOnlyList<EnumDefinition> AllEnums => _current.AllEnums;

    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => _current.AllHardcodedSets;

    public IReadOnlyList<MetafileDefinition> AllMetafiles => _current.AllMetafiles;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Fetching schema manifest from {BaseUrl}", _baseUrl);
            var indexJson = await _http.GetStringAsync(_baseUrl + "_index.json", ct);
            var manifest = JsonSerializer.Deserialize<SchemaManifest>(indexJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null) return;

            if (_cache.TryLoad(indexJson, manifest, out var cached))
            {
                _logger.LogInformation("Schema loaded from local cache");
                _current = cached;
                SchemaRefreshed?.Invoke(this, EventArgs.Empty);
                _readyTcs.TrySetResult();
                return;
            }

            await BuildIndexAsync(manifest, indexJson, ct);
        }
        catch
        {
            // Always signal ReadyAsync so WorkspaceScanner.WaitForSchemaAsync does not
            // block for its full 30-second timeout when the HTTP fetch fails.
            _readyTcs.TrySetResult();
            throw;
        }
    }

    private async Task BuildIndexAsync(SchemaManifest manifest, string indexJson, CancellationToken ct)
    {
        _logger.LogDebug(
            "Building schema index: {TagFileCount} tag files, {TypeFileCount} type files, {EnumFileCount} enum files",
            manifest.Tags.Count, manifest.Types.Count, manifest.Enums.Count);

        var tagsByType = new List<(string, IReadOnlyList<RawTagDefinition>)>();
        var types = new List<GameObjectTypeDefinition>();
        var enums = new List<RawEnumDefinition>();
        var hardcodedSets = new List<HardcodedReferenceSet>();
        var metafiles = new List<MetafileDefinition>();
        var fetchedFiles = new List<(string relativePath, string content)>();

        foreach (var path in manifest.Tags)
        {
            var typeName = Path.GetFileNameWithoutExtension(path);
            var (parsed, raw) = await FetchYamlAsync(path, yaml => YamlSchemaParser.ParseTagFile(yaml), ct);
            if (parsed is null)
            {
                _rawTagFallbacks.TryGetValue(typeName, out var fallback);
                if (fallback is null || fallback.Count == 0)
                    _logger.LogWarning(
                        "304 Not Modified for '{Path}' but no prior schema in memory — treating as empty", path);
                tagsByType.Add((typeName, fallback ?? []));
            }
            else
            {
                tagsByType.Add((typeName, parsed));
            }

            if (raw is not null)
                fetchedFiles.Add((path, raw));
        }

        foreach (var path in manifest.Types)
        {
            var (parsed, raw) = await FetchYamlAsync(path, YamlSchemaParser.ParseTypeFile, ct);
            if (parsed is null)
            {
                var fallback = _current.AllObjectTypes;
                if (fallback.Count == 0)
                    _logger.LogWarning(
                        "304 Not Modified for '{Path}' but no prior schema in memory — treating as empty", path);
                types.AddRange(fallback);
            }
            else
            {
                types.AddRange(parsed);
            }

            if (raw is not null)
                fetchedFiles.Add((path, raw));
        }

        foreach (var path in manifest.Enums)
        {
            var (parsed, raw) = await FetchYamlAsync<RawEnumDefinition>(
                path, yaml => [YamlSchemaParser.ParseEnumFile(yaml)], ct);
            if (parsed is null)
            {
                if (_rawEnumFallbacks.Count == 0)
                    _logger.LogWarning(
                        "304 Not Modified for '{Path}' but no prior schema in memory — treating as empty", path);
                enums.AddRange(_rawEnumFallbacks);
            }
            else
            {
                enums.AddRange(parsed);
            }

            if (raw is not null)
                fetchedFiles.Add((path, raw));
        }

        foreach (var path in manifest.Hardcoded)
        {
            var (parsed, raw) = await FetchYamlAsync<HardcodedReferenceSet>(
                path, yaml => [YamlSchemaParser.ParseHardcodedSetFile(yaml)], ct);
            if (parsed is null)
            {
                var fallback = _current.AllHardcodedSets;
                if (fallback.Count == 0)
                    _logger.LogWarning(
                        "304 Not Modified for '{Path}' but no prior schema in memory — treating as empty", path);
                hardcodedSets.AddRange(fallback);
            }
            else
            {
                hardcodedSets.AddRange(parsed);
            }

            if (raw is not null)
                fetchedFiles.Add((path, raw));
        }

        foreach (var path in manifest.Meta)
        {
            var (parsed, raw) = await FetchYamlAsync<MetafileDefinition>(
                path, yaml => [.. YamlSchemaParser.ParseMetafileFile(yaml)], ct);
            if (parsed is null)
                metafiles.AddRange(_current.AllMetafiles);
            else
                metafiles.AddRange(parsed);

            if (raw is not null)
                fetchedFiles.Add((path, raw));
        }

        _rawTagFallbacks.Clear();
        foreach (var (tn, tags) in tagsByType)
            _rawTagFallbacks[tn] = tags;
        _rawEnumFallbacks = [.. enums];

        _current = new SchemaIndex(tagsByType, types, enums, hardcodedSets, metafiles);
        SchemaRefreshed?.Invoke(this, EventArgs.Empty);
        _readyTcs.TrySetResult();

        _cache.Update(indexJson, fetchedFiles, manifest.BaselineHash);

        _logger.LogInformation(
            "Schema index built: {TagCount} tags, {TypeCount} types, {EnumCount} enums, {HardcodedCount} hardcoded set(s)",
            _current.AllTags.Count, _current.AllObjectTypes.Count, _current.AllEnums.Count,
            _current.AllHardcodedSets.Count);
    }

    // Returns (parsed, rawYaml). rawYaml is null on 304 (ETag hit); parsed is null on 304 too.
    private async Task<(List<T>? parsed, string? raw)> FetchYamlAsync<T>(
        string relativePath, Func<string, List<T>> parse, CancellationToken ct)
    {
        var url = _baseUrl + relativePath;
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (_etags.TryGetValue(url, out var etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return (null, null);

        response.EnsureSuccessStatusCode();
        if (response.Headers.ETag?.Tag is { } newEtag)
            _etags[url] = newEtag;

        var yaml = await response.Content.ReadAsStringAsync(ct);
        return (parse(yaml), yaml);
    }
}