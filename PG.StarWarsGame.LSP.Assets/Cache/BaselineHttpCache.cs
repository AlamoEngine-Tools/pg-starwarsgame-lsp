using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Cache;

public sealed class BaselineHttpCache
{
    private const string CacheFile = "baseline.json";
    private const string ChecksumFile = "baseline.sha256";
    private readonly string _dir;
    private readonly IFileSystem _fs;

    public BaselineHttpCache(IFileSystem fs)
    {
        _fs = fs;
        _dir = _fs.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".pg-swg-lsp", "baseline");
    }

    private string FilePath => _fs.Path.Combine(_dir, CacheFile);
    private string ChecksumPath => _fs.Path.Combine(_dir, ChecksumFile);

    /// <summary>
    ///     Returns true and deserializes <paramref name="baseline" /> from disk when the stored
    ///     checksum matches <paramref name="jsonContent" /> and the cache file exists.
    /// </summary>
    public bool TryLoad(string jsonContent, out SerializedBaseline? baseline)
    {
        baseline = null;
        try
        {
            if (!_fs.File.Exists(ChecksumPath) || !_fs.File.Exists(FilePath))
                return false;

            var stored = _fs.File.ReadAllText(ChecksumPath).Trim();
            if (!string.Equals(stored, Sha256Of(jsonContent), StringComparison.OrdinalIgnoreCase))
                return false;

            baseline = JsonSerializer.Deserialize<SerializedBaseline>(_fs.File.ReadAllText(FilePath));
            return baseline is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Writes <paramref name="jsonContent" /> and its checksum to the cache directory.</summary>
    public void Update(string jsonContent)
    {
        _fs.Directory.CreateDirectory(_dir);
        _fs.File.WriteAllText(FilePath, jsonContent);
        _fs.File.WriteAllText(ChecksumPath, Sha256Of(jsonContent));
    }

    private static string Sha256Of(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}