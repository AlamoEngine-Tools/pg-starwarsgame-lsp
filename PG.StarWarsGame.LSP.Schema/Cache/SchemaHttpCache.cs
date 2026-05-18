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
    ///     <paramref name="indexJson" /> plus the contents of every file listed in the manifest.
    /// </summary>
    public bool TryLoad(string indexJson, SchemaManifest manifest, out SchemaIndex index)
    {
        index = SchemaIndex.Empty;
        try
        {
            if (!_fs.File.Exists(ChecksumPath))
                return false;

            var allRels = manifest.Tags.Concat(manifest.Types).Concat(manifest.Enums).ToList();

            foreach (var rel in allRels)
                if (!_fs.File.Exists(_fs.Path.Combine(_dir, rel)))
                    return false;

            // Read all files once; reuse strings for both checksum and parsing.
            var contents = allRels
                .Select(rel => _fs.File.ReadAllText(_fs.Path.Combine(_dir, rel)))
                .ToList();

            var stored = _fs.File.ReadAllText(ChecksumPath).Trim();
            if (!string.Equals(stored, ComputeChecksum(indexJson, contents), StringComparison.OrdinalIgnoreCase))
                return false;

            var tagsByType = new List<(string, IReadOnlyList<XmlTagDefinition>)>();
            var types = new List<GameObjectTypeDefinition>();
            var enums = new List<EnumDefinition>();

            var i = 0;
            foreach (var rel in manifest.Tags)
            {
                var typeName = _fs.Path.GetFileNameWithoutExtension(rel);
                tagsByType.Add((typeName, YamlSchemaParser.ParseTagFile(contents[i++])));
            }

            foreach (var _ in manifest.Types)
                types.AddRange(YamlSchemaParser.ParseTypeFile(contents[i++]));
            foreach (var _ in manifest.Enums)
                enums.Add(YamlSchemaParser.ParseEnumFile(contents[i++]));

            index = new SchemaIndex(tagsByType, types, enums);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load schema from local cache; will fetch from remote");
            return false;
        }
    }

    /// <summary>
    ///     Writes <paramref name="indexJson" />, all YAML files, and the new checksum to disk,
    ///     mirroring the manifest's relative paths under the cache directory.
    /// </summary>
    public void Update(string indexJson, IReadOnlyList<(string relativePath, string content)> yamlFiles)
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

        _fs.File.WriteAllText(ChecksumPath, ComputeChecksum(indexJson, yamlFiles.Select(f => f.content)));
    }

    // Hashes indexJson followed by each file's content in manifest order so that
    // any change to any individual file invalidates the cached snapshot.
    private static string ComputeChecksum(string indexJson, IEnumerable<string> fileContents)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(indexJson));
        foreach (var content in fileContents)
            hash.AppendData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}