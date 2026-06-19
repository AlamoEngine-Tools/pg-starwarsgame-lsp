// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace PG.StarWarsGame.LSP.Core.Caching;

public static class ProjectFileHasher
{
    public static string ComputeFileHash(string absolutePath, IFileSystem fs)
    {
        var bytes = fs.File.ReadAllBytes(absolutePath);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    // Sorts entries by relative path so the hash is stable regardless of enumeration order.
    public static string ComputeProjectHash(IEnumerable<(string relativePath, string fileHash)> entries)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (path, hash) in entries.OrderBy(e => e.relativePath, StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(path));
            sha.AppendData(Encoding.UTF8.GetBytes("\0"));
            sha.AppendData(Encoding.UTF8.GetBytes(hash));
            sha.AppendData(Encoding.UTF8.GetBytes("\n"));
        }

        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}