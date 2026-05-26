// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Providers;

/// <summary>
///     Loads the schema from a local directory and hot-reloads on file changes.
///     Expects YAML files matching the same layout as the remote schema repository:
///     <c>tags/*.yaml</c>, <c>types.yaml</c>, <c>enums/*.yaml</c>.
/// </summary>
public sealed class LocalFileSchemaProvider : ISchemaProvider, IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<LocalFileSchemaProvider> _logger;
    private readonly string _rootPath;
    private readonly IFileSystemWatcher _watcher;
    private volatile SchemaIndex _current = SchemaIndex.Empty;

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

    public event EventHandler? SchemaRefreshed;

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

    public void Load()
    {
        _logger.LogDebug("Loading schema from {Path}", _rootPath);

        var tagsByType = new List<(string, IReadOnlyList<RawTagDefinition>)>();
        var types = new List<GameObjectTypeDefinition>();
        var enums = new List<RawEnumDefinition>();
        var hardcodedSets = new List<HardcodedReferenceSet>();

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
        }

        var metaPath = _fileSystem.Path.Combine(_rootPath, "meta", "metafiles.yaml");
        var metafiles = _fileSystem.File.Exists(metaPath)
            ? YamlSchemaParser.ParseMetafileFile(_fileSystem.File.ReadAllText(metaPath))
            : (IReadOnlyList<MetafileDefinition>)[];

        _current = new SchemaIndex(tagsByType, types, enums, hardcodedSets, metafiles);
        SchemaRefreshed?.Invoke(this, EventArgs.Empty);

        _logger.LogInformation(
            "Schema loaded: {TagCount} tags across {TypeCount} types, {EnumCount} enums, {HardcodedCount} hardcoded set(s) from {Path}",
            _current.AllTags.Count, _current.AllObjectTypes.Count, _current.AllEnums.Count,
            _current.AllHardcodedSets.Count, _rootPath);
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