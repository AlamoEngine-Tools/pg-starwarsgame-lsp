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

        var workspaceRoot = ResolveWorkspaceRoot(initializationOptions);
        var fromFile = LoadConfigFile(workspaceRoot);
        var overlay = ParseInitOptions(initializationOptions);
        Current = Merge(fromFile, overlay);

        _logger.LogInformation("LSP configuration loaded (locale={Locale}, gamePath={GamePath})",
            Current.Locale, Current.GamePath ?? "<none>");
    }

    private static string? ResolveWorkspaceRoot(object? initOptions)
    {
        if (initOptions is JsonElement elem &&
            elem.TryGetProperty("workspaceRoot", out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private LspConfiguration LoadConfigFile(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return new LspConfiguration();

        var path = _fileSystem.Path.Combine(workspaceRoot, ".pg-lsp.json");
        if (!_fileSystem.File.Exists(path)) return new LspConfiguration();

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

    private LspConfiguration ParseInitOptions(object? initOptions)
    {
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

        var baseGamePath = TryGetString(elem, "baseGamePath");
        var expansionGamePath = TryGetString(elem, "expansionGamePath");
        var locale = TryGetString(elem, "locale");
        var schemaUrl = TryGetString(elem, "schemaUrl");
        var schemaLocalPath = TryGetString(elem, "schemaLocalPath");
        var baselineLocalPath = TryGetString(elem, "baselineLocalPath");
        var baselineType = TryGetString(elem, "baselineType");

        var modPaths = new List<string>();
        if (elem.TryGetProperty("modPaths", out var modPathsElem) &&
            modPathsElem.ValueKind == JsonValueKind.Array)
            foreach (var item in modPathsElem.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                    modPaths.Add(s);

        var xmlDirectories = new List<string>();
        if (elem.TryGetProperty("xmlDirectories", out var xmlDirsElem) &&
            xmlDirsElem.ValueKind == JsonValueKind.Array)
            foreach (var item in xmlDirsElem.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } d)
                    xmlDirectories.Add(d);

        _logger.LogInformation(
            "ParseInitOptions: schemaLocalPath={LocalPath}, schemaUrl={Url}, baselineType={BaselineType}",
            schemaLocalPath ?? "<null>", schemaUrl ?? "<null>", baselineType ?? "<null>");

        return new LspConfiguration
        {
            GamePath = baseGamePath,
            ExpansionPath = expansionGamePath,
            ModPaths = modPaths,
            XmlDirectories = xmlDirectories,
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
                    : new BaselineSourceConfig()
        };
    }

    private static LspConfiguration Merge(LspConfiguration file, LspConfiguration overlay)
    {
        return new LspConfiguration
        {
            GamePath = overlay.GamePath ?? file.GamePath,
            ExpansionPath = overlay.ExpansionPath ?? file.ExpansionPath,
            ModPaths = overlay.ModPaths.Count > 0 ? overlay.ModPaths : file.ModPaths,
            XmlDirectories = overlay.XmlDirectories.Count > 0 ? overlay.XmlDirectories : file.XmlDirectories,
            Locale = overlay.Locale != "en" ? overlay.Locale : file.Locale,
            SchemaSource = overlay.SchemaSource.Type == SchemaSourceType.Local
                           || overlay.SchemaSource.Url != new SchemaSourceConfig().Url
                ? overlay.SchemaSource
                : file.SchemaSource,
            BaselineSource = overlay.BaselineSource.Type != BaselineSourceType.Http
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
}