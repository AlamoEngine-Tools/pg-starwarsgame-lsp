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
    ///     <paramref name="indexJson" /> and every file listed in the manifest exists on disk.
    /// </summary>
    public bool TryLoad(string indexJson, SchemaManifest manifest, out SchemaIndex index)
    {
        index = SchemaIndex.Empty;
        try
        {
            if (!_fs.File.Exists(ChecksumPath))
                return false;

            var stored = _fs.File.ReadAllText(ChecksumPath).Trim();
            if (!string.Equals(stored, Sha256Of(indexJson), StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var rel in manifest.Tags.Concat(manifest.Types).Concat(manifest.Enums))
                if (!_fs.File.Exists(_fs.Path.Combine(_dir, rel)))
                    return false;

            var tagsByType = new List<(string, IReadOnlyList<XmlTagDefinition>)>();
            var types = new List<GameObjectTypeDefinition>();
            var enums = new List<EnumDefinition>();

            foreach (var rel in manifest.Tags)
            {
                var typeName = _fs.Path.GetFileNameWithoutExtension(rel);
                var yaml = _fs.File.ReadAllText(_fs.Path.Combine(_dir, rel));
                tagsByType.Add((typeName, YamlSchemaParser.ParseTagFile(yaml)));
            }

            foreach (var rel in manifest.Types)
            {
                var yaml = _fs.File.ReadAllText(_fs.Path.Combine(_dir, rel));
                types.AddRange(YamlSchemaParser.ParseTypeFile(yaml));
            }

            foreach (var rel in manifest.Enums)
            {
                var yaml = _fs.File.ReadAllText(_fs.Path.Combine(_dir, rel));
                enums.Add(YamlSchemaParser.ParseEnumFile(yaml));
            }

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

        _fs.File.WriteAllText(ChecksumPath, Sha256Of(indexJson));
    }

    private static string Sha256Of(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
