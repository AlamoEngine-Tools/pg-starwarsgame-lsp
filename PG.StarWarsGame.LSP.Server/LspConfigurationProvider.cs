// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     Resolves <see cref="LspConfiguration" /> by merging two sources:
///     1. <c>.pg-lsp.json</c> in the workspace root (static defaults).
///     2. <c>initializationOptions</c> from the editor's LSP initialize request (dynamic overrides).
///     Editor values win over file values.
/// </summary>
public sealed class LspConfigurationProvider : ILspConfigurationProvider
{
    private static readonly JsonSerializerOptions FeatureJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<LspConfigurationProvider> _logger;

    public LspConfigurationProvider(IFileSystem fileSystem, ILogger<LspConfigurationProvider> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public LspConfiguration Current { get; private set; } = new();

    /// <summary>
    ///     Called once the workspace root is known (from the initialize request).
    ///     Reads .pg-lsp.json if present, then overlays initializationOptions.
    /// </summary>
    public void LoadFrom(object? initializationOptions)
    {
        _logger.LogDebug("Loading LSP configuration");

        var fromFile = LoadConfigFile(ResolveConfigFileRoots(initializationOptions));
        var overlay = ParseInitOptions(initializationOptions, out var overlayFeatures);
        Current = Merge(fromFile, overlay, overlayFeatures);

        _logger.LogInformation("LSP configuration loaded (locale={Locale}, gamePath={GamePath})",
            Current.Locale, Current.GamePath ?? "<none>");
    }

    // workspaceRoot first, then the rest of the folders: .pg-lsp.json configures the session (game
    // paths, locale, feature flags), which is window-wide rather than per project, so the first
    // folder that has one wins and the others are ignored.
    private static IReadOnlyList<string> ResolveConfigFileRoots(object? initOptions)
    {
        if (initOptions is not JsonElement elem) return [];

        var roots = new List<string>();
        if (elem.TryGetProperty("workspaceRoot", out var single) && single.ValueKind == JsonValueKind.String &&
            single.GetString() is { Length: > 0 } first)
            roots.Add(first);

        foreach (var extra in TryGetStringArray(elem, "workspaceRoots"))
            if (!roots.Contains(extra, StringComparer.OrdinalIgnoreCase))
                roots.Add(extra);

        return roots;
    }

    private LspConfiguration LoadConfigFile(IReadOnlyList<string> workspaceRoots)
    {
        foreach (var workspaceRoot in workspaceRoots)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot)) continue;

            var path = _fileSystem.Path.Combine(workspaceRoot, ".pg-lsp.json");
            if (!_fileSystem.File.Exists(path)) continue;

            _logger.LogDebug("Reading .pg-lsp.json from {WorkspaceRoot}", workspaceRoot);
            try
            {
                var json = _fileSystem.File.ReadAllText(path);
                return JsonSerializer.Deserialize<LspConfiguration>(json) ?? new LspConfiguration();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse .pg-lsp.json at {Path}; using defaults", path);
                return new LspConfiguration();
            }
        }

        return new LspConfiguration();
    }

    private LspConfiguration ParseInitOptions(object? initOptions, out FeatureFlags? features)
    {
        features = null;
        if (initOptions is null) return new LspConfiguration();

        JsonElement elem;
        if (initOptions is JsonElement je)
        {
            elem = je;
        }
        else
        {
            // OmniSharp may deliver initializationOptions as a Newtonsoft JToken or other type.
            // Round-trip through the object's string representation to obtain a JsonElement.
            _logger.LogDebug("InitializationOptions type is {Type}; converting via ToString()",
                initOptions.GetType().Name);
            try
            {
                var json = initOptions.ToString() ?? "{}";
                elem = JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert InitializationOptions of type {Type}; using defaults",
                    initOptions.GetType().Name);
                return new LspConfiguration();
            }
        }

        features = ParseFeatures(elem);

        var workspaceRoot = TryGetString(elem, "workspaceRoot");
        var workspaceRoots = TryGetStringArray(elem, "workspaceRoots");
        var baseGamePath = TryGetString(elem, "baseGamePath");
        var expansionGamePath = TryGetString(elem, "expansionGamePath");
        var locale = TryGetString(elem, "locale");
        var schemaUrl = TryGetString(elem, "schemaUrl");
        var schemaLocalPath = TryGetString(elem, "schemaLocalPath");
        var baselineLocalPath = TryGetString(elem, "baselineLocalPath");
        var baselineType = TryGetString(elem, "baselineType");
        var baselineUrl = TryGetString(elem, "baselineUrl");

        _logger.LogInformation(
            "ParseInitOptions: schemaLocalPath={LocalPath}, schemaUrl={Url}, baselineType={BaselineType}",
            schemaLocalPath ?? "<null>", schemaUrl ?? "<null>", baselineType ?? "<null>");

        return new LspConfiguration
        {
            WorkspaceRoot = workspaceRoot,
            WorkspaceRoots = workspaceRoots,
            GamePath = baseGamePath,
            ExpansionPath = expansionGamePath,
            Locale = locale ?? "en",
            SchemaSource = !string.IsNullOrWhiteSpace(schemaLocalPath)
                ? new SchemaSourceConfig { Type = SchemaSourceType.Local, LocalPath = schemaLocalPath }
                : string.IsNullOrWhiteSpace(schemaUrl)
                    ? new SchemaSourceConfig()
                    : new SchemaSourceConfig { Url = schemaUrl },
            BaselineSource = string.Equals(baselineType, "None", StringComparison.OrdinalIgnoreCase)
                ? new BaselineSourceConfig { Type = BaselineSourceType.None }
                : !string.IsNullOrWhiteSpace(baselineLocalPath)
                    ? new BaselineSourceConfig { Type = BaselineSourceType.Local, LocalPath = baselineLocalPath }
                    : !string.IsNullOrWhiteSpace(baselineUrl)
                        ? new BaselineSourceConfig { Url = baselineUrl }
                        : new BaselineSourceConfig()
        };
    }

    /// <summary>
    ///     Extracts the optional <c>features</c> node from initializationOptions. Returns
    ///     <c>null</c> when the node is absent or malformed, so the .pg-lsp.json value (or the
    ///     all-true defaults) applies. Client keys are camelCase; parsing is case-insensitive.
    /// </summary>
    private FeatureFlags? ParseFeatures(JsonElement elem)
    {
        if (!elem.TryGetProperty("features", out var node)) return null;
        try
        {
            return node.Deserialize<FeatureFlags>(FeatureJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed 'features' node in InitializationOptions; ignoring it");
            return null;
        }
    }

    private static LspConfiguration Merge(LspConfiguration file, LspConfiguration overlay,
        FeatureFlags? overlayFeatures)
    {
        return new LspConfiguration
        {
            // A features node sent by the client wins wholesale over the file's Features:
            // the client always sends the complete resolved object, so no per-leaf merge.
            Features = overlayFeatures ?? file.Features,
            WorkspaceRoot = overlay.WorkspaceRoot ?? file.WorkspaceRoot,
            WorkspaceRoots = overlay.WorkspaceRoots.Count > 0 ? overlay.WorkspaceRoots : file.WorkspaceRoots,
            GamePath = overlay.GamePath ?? file.GamePath,
            ExpansionPath = overlay.ExpansionPath ?? file.ExpansionPath,
            Locale = overlay.Locale != "en" ? overlay.Locale : file.Locale,
            SchemaSource = overlay.SchemaSource.Type == SchemaSourceType.Local
                           || overlay.SchemaSource.Url != new SchemaSourceConfig().Url
                ? overlay.SchemaSource
                : file.SchemaSource,
            BaselineSource = overlay.BaselineSource.Type != BaselineSourceType.Http
                             || overlay.BaselineSource.Url != new BaselineSourceConfig().Url
                ? overlay.BaselineSource
                : file.BaselineSource
        };
    }

    private static string? TryGetString(JsonElement elem, string property)
    {
        return elem.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    private static IReadOnlyList<string> TryGetStringArray(JsonElement elem, string property)
    {
        if (!elem.TryGetProperty(property, out var p) || p.ValueKind != JsonValueKind.Array)
            return [];

        var values = new List<string>();
        foreach (var item in p.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                values.Add(value);

        return values;
    }
}