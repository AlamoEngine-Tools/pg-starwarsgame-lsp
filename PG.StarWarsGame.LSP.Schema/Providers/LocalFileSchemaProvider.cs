// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Versioning;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Providers;

/// <summary>
///     Loads the schema from a local directory and hot-reloads on file changes.
///     Expects YAML files matching the same layout as the remote schema repository:
///     <c>tags/*.yaml</c>, <c>types.yaml</c>, <c>kinds.yaml</c>, <c>enums/*.yaml</c>.
/// </summary>
public sealed class LocalFileSchemaProvider : SchemaIndexProviderBase, IVersionedSchemaProvider, IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<LocalFileSchemaProvider> _logger;
    private readonly string _rootPath;
    private readonly IFileSystemWatcher _watcher;

    public LocalFileSchemaProvider(string rootPath, IFileSystem fileSystem,
        ILogger<LocalFileSchemaProvider> logger)
    {
        _rootPath = rootPath;
        _fileSystem = fileSystem;
        _logger = logger;
        _watcher = _fileSystem.FileSystemWatcher.New(rootPath, "*.yaml");
        _watcher.IncludeSubdirectories = true;
        _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
        _watcher.EnableRaisingEvents = true;
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += (_, _) => Reload();
        Load();
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }

    /// <inheritdoc />
    public SchemaVersionCheck? LastVersionCheck { get; private set; }

    public void Load()
    {
        _logger.LogDebug("Loading schema from {Path}", _rootPath);

        // Files come from the directory walk below, not from _index.json - but the manifest is
        // still where the contract version lives, so read it for that alone. An incompatible
        // schema leaves the previous index untouched rather than half-replacing it.
        LastVersionCheck = ReadVersion();
        if (!LastVersionCheck.CanLoad)
        {
            _logger.LogError("Schema rejected: {Message}", LastVersionCheck.Message);
            return;
        }

        if (LastVersionCheck.Message.Length > 0)
            _logger.LogWarning("{Message}", LastVersionCheck.Message);

        var tagsByType = new List<(string, IReadOnlyList<RawTagDefinition>)>();
        var types = new List<GameObjectTypeDefinition>();
        var enums = new List<RawEnumDefinition>();
        var hardcodedSets = new List<HardcodedReferenceSet>();
        var kinds = new List<ObjectKindDefinition>();

        foreach (var file in _fileSystem.Directory.EnumerateFiles(_rootPath, "*.yaml", SearchOption.AllDirectories))
        {
            var relativePath = _fileSystem.Path.GetRelativePath(_rootPath, file);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (parts.Length == 2 && parts[0].Equals("tags", StringComparison.OrdinalIgnoreCase))
            {
                var typeName = _fileSystem.Path.GetFileNameWithoutExtension(file);
                tagsByType.Add((typeName, YamlSchemaParser.ParseTagFile(_fileSystem.File.ReadAllText(file), _logger)));
            }
            else if (parts.Length == 2 && parts[0].Equals("enums", StringComparison.OrdinalIgnoreCase))
            {
                enums.Add(YamlSchemaParser.ParseEnumFile(_fileSystem.File.ReadAllText(file), _logger));
            }
            else if (parts.Length == 2 && parts[0].Equals("hardcoded", StringComparison.OrdinalIgnoreCase))
            {
                hardcodedSets.Add(YamlSchemaParser.ParseHardcodedSetFile(_fileSystem.File.ReadAllText(file)));
            }
            else if (parts.Length == 1 && parts[0].Equals("types.yaml", StringComparison.OrdinalIgnoreCase))
            {
                types.AddRange(YamlSchemaParser.ParseTypeFile(_fileSystem.File.ReadAllText(file)));
            }
            else if (parts.Length == 1 && parts[0].Equals("kinds.yaml", StringComparison.OrdinalIgnoreCase))
            {
                kinds.AddRange(YamlSchemaParser.ParseKindFile(_fileSystem.File.ReadAllText(file)));
            }
        }

        var metaPath = _fileSystem.Path.Combine(_rootPath, "meta", "metafiles.yaml");
        var metafiles = _fileSystem.File.Exists(metaPath)
            ? YamlSchemaParser.ParseMetafileFile(_fileSystem.File.ReadAllText(metaPath))
            : (IReadOnlyList<MetafileDefinition>)[];

        Publish(new SchemaIndex(tagsByType, types, enums, hardcodedSets, metafiles, kinds));

        _logger.LogInformation(
            "Schema loaded: {TagCount} tags across {TypeCount} types, {KindCount} kinds, {EnumCount} enums, {HardcodedCount} hardcoded set(s) from {Path}",
            Current.AllTags.Count, Current.AllObjectTypes.Count, Current.AllKinds.Count, Current.AllEnums.Count,
            Current.AllHardcodedSets.Count, _rootPath);
    }

    /// <summary>
    ///     Reads <c>_index.json</c> for its declared contract version. A schema directory with no
    ///     manifest at all is a legitimate local layout (the walk finds the YAML regardless), so
    ///     that reads as unversioned - but a manifest that is present and unreadable is refused,
    ///     since treating corruption as "no version declared" would wave the schema through.
    /// </summary>
    private SchemaVersionCheck ReadVersion()
    {
        var manifestPath = _fileSystem.Path.Combine(_rootPath, "_index.json");
        if (!_fileSystem.File.Exists(manifestPath))
            return SchemaVersionGate.Check(null);

        try
        {
            var manifest = JsonSerializer.Deserialize<SchemaManifest>(
                _fileSystem.File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return SchemaVersionGate.Check(manifest?.SchemaVersion);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Could not read schema manifest '{Path}'", manifestPath);
            return new SchemaVersionCheck(
                SchemaVersionCompatibility.Malformed, null, SchemaVersionGate.SupportedRange,
                $"The game schema manifest at '{manifestPath}' could not be read, so the schema " +
                "cannot be checked for compatibility. XML support is disabled.");
        }
    }

    private void OnFileChanged(object _, FileSystemEventArgs __)
    {
        Reload();
    }

    private void Reload()
    {
        _logger.LogDebug("Hot-reloading schema from {Path}", _rootPath);
        try
        {
            Load();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Schema hot-reload failed; retaining previous index");
        }
    }
}