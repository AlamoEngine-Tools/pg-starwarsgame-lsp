// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Cache;

public sealed class SchemaHttpCache
{
    private readonly string _dir;
    private readonly IFileSystem _fs;
    private readonly ILogger<SchemaHttpCache> _logger;

    public SchemaHttpCache(IFileSystem fs, ILogger<SchemaHttpCache> logger)
    {
        _fs = fs;
        _logger = logger;
        _dir = _fs.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".pg-swg-lsp", "schema");
    }

    private string ChecksumPath => _fs.Path.Combine(_dir, "_index.sha256");

    /// <summary>
    ///     Returns true and populates <paramref name="index" /> when the stored checksum matches
    ///     the manifest's <see cref="SchemaManifest.BaselineHash" /> (fast path) or the
    ///     SHA-256 of every file listed in the manifest (slow path).
    /// </summary>
    public bool TryLoad(string indexJson, SchemaManifest manifest, out SchemaIndex index)
    {
        index = SchemaIndex.Empty;
        try
        {
            if (!_fs.File.Exists(ChecksumPath))
                return false;

            var stored = _fs.File.ReadAllText(ChecksumPath).Trim();

            var allRels = manifest.Tags
                .Concat(manifest.Types)
                .Concat(manifest.Enums)
                .Concat(manifest.Hardcoded)
                .Concat(manifest.Meta)
                .ToList();

            foreach (var rel in allRels)
                if (!_fs.File.Exists(_fs.Path.Combine(_dir, rel)))
                    return false;

            if (manifest.BaselineHash is not null)
            {
                // Fast path: the manifest carries a pre-computed hash — compare directly.
                if (!string.Equals(stored, manifest.BaselineHash, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else
            {
                // Slow path: re-hash every file from disk.
                var contents = allRels
                    .Select(rel => _fs.File.ReadAllText(_fs.Path.Combine(_dir, rel)))
                    .ToList();
                if (!string.Equals(stored, ComputeYamlHash(contents), StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Parse all five categories from disk.
            var tagsByType = new List<(string, IReadOnlyList<RawTagDefinition>)>();
            var types = new List<GameObjectTypeDefinition>();
            var enums = new List<RawEnumDefinition>();
            var hardcodedSets = new List<HardcodedReferenceSet>();
            var metafiles = new List<MetafileDefinition>();

            foreach (var rel in manifest.Tags)
            {
                var typeName = _fs.Path.GetFileNameWithoutExtension(rel);
                var content = _fs.File.ReadAllText(_fs.Path.Combine(_dir, rel));
                tagsByType.Add((typeName, YamlSchemaParser.ParseTagFile(content)));
            }

            foreach (var rel in manifest.Types)
                types.AddRange(YamlSchemaParser.ParseTypeFile(_fs.File.ReadAllText(_fs.Path.Combine(_dir, rel))));
            foreach (var rel in manifest.Enums)
                enums.Add(YamlSchemaParser.ParseEnumFile(_fs.File.ReadAllText(_fs.Path.Combine(_dir, rel))));
            foreach (var rel in manifest.Hardcoded)
                hardcodedSets.Add(
                    YamlSchemaParser.ParseHardcodedSetFile(_fs.File.ReadAllText(_fs.Path.Combine(_dir, rel))));
            foreach (var rel in manifest.Meta)
                metafiles.AddRange(
                    YamlSchemaParser.ParseMetafileFile(_fs.File.ReadAllText(_fs.Path.Combine(_dir, rel))));

            index = new SchemaIndex(tagsByType, types, enums, hardcodedSets, metafiles);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load schema from local cache; will fetch from remote");
            return false;
        }
    }

    /// <summary>
    ///     Writes <paramref name="indexJson" />, all YAML files, and the checksum to disk.
    ///     When <paramref name="baselineHash" /> is supplied it is stored directly; otherwise
    ///     the checksum is computed as SHA-256 of all YAML file contents in order.
    /// </summary>
    public void Update(string indexJson, IReadOnlyList<(string relativePath, string content)> yamlFiles,
        string? baselineHash = null)
    {
        _fs.Directory.CreateDirectory(_dir);
        _fs.File.WriteAllText(_fs.Path.Combine(_dir, "_index.json"), indexJson);

        foreach (var (rel, content) in yamlFiles)
        {
            var fullPath = _fs.Path.Combine(_dir, rel);
            var parentDir = _fs.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parentDir))
                _fs.Directory.CreateDirectory(parentDir);
            _fs.File.WriteAllText(fullPath, content);
        }

        var hash = baselineHash ?? ComputeYamlHash(yamlFiles.Select(f => f.content));
        _fs.File.WriteAllText(ChecksumPath, hash);
    }

    // Hashes each YAML file's content in manifest order — indexJson is excluded so
    // the formula is compatible with the pre-computed baselineHash in _index.json.
    private static string ComputeYamlHash(IEnumerable<string> fileContents)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var content in fileContents)
            hash.AppendData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}