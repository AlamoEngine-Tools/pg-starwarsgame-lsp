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
    private readonly ILogger<LspConfigurationProvider> _logger;

    public LspConfigurationProvider(ILogger<LspConfigurationProvider> logger)
    {
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

        var path = Path.Combine(workspaceRoot, ".pg-lsp.json");
        if (!File.Exists(path)) return new LspConfiguration();

        _logger.LogDebug("Reading .pg-lsp.json from {WorkspaceRoot}", workspaceRoot);
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LspConfiguration>(json) ?? new LspConfiguration();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse .pg-lsp.json at {Path}; using defaults", path);
            return new LspConfiguration();
        }
    }

    private static LspConfiguration ParseInitOptions(object? initOptions)
    {
        if (initOptions is not JsonElement elem) return new LspConfiguration();

        var gamePath = TryGetString(elem, "gamePath");
        var locale = TryGetString(elem, "locale");
        var schemaUrl = TryGetString(elem, "schemaUrl");
        var baselineLocalPath = TryGetString(elem, "baselineLocalPath");

        var modPaths = new List<string>();
        if (elem.TryGetProperty("modPaths", out var modPathsElem) &&
            modPathsElem.ValueKind == JsonValueKind.Array)
            foreach (var item in modPathsElem.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                    modPaths.Add(s);

        return new LspConfiguration
        {
            GamePath = gamePath,
            ModPaths = modPaths,
            Locale = locale ?? "en",
            SchemaSource = string.IsNullOrWhiteSpace(schemaUrl)
                ? new SchemaSourceConfig()
                : new SchemaSourceConfig { Url = schemaUrl },
            BaselineSource = string.IsNullOrWhiteSpace(baselineLocalPath)
                ? new BaselineSourceConfig()
                : new BaselineSourceConfig
                {
                    Type = BaselineSourceType.Local,
                    LocalPath = baselineLocalPath
                }
        };
    }

    private static LspConfiguration Merge(LspConfiguration file, LspConfiguration overlay)
    {
        return new LspConfiguration
        {
            GamePath = overlay.GamePath ?? file.GamePath,
            ModPaths = overlay.ModPaths.Count > 0 ? overlay.ModPaths : file.ModPaths,
            Locale = overlay.Locale != "en" ? overlay.Locale : file.Locale,
            SchemaSource = overlay.SchemaSource.Url != new SchemaSourceConfig().Url
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