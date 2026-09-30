// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Compression;
using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     Reads and writes <see cref="ModelBoneCatalogSnapshot" />, gzipped MessagePack like
///     <see cref="ProjectIndexSerializer" />.
/// </summary>
public static class ModelBoneCatalogSerializer
{
    public static byte[] Serialize(ModelBoneCatalogSnapshot snapshot)
    {
        var msgpack = MessagePackSerializer.Serialize(snapshot);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
        {
            gz.Write(msgpack);
        }

        return ms.ToArray();
    }

    /// <summary>
    ///     Returns the snapshot, or <see langword="null" /> when it cannot be read or was written
    ///     by a different extractor version. A miss is never an error - it just means re-extract.
    /// </summary>
    public static ModelBoneCatalogSnapshot? Deserialize(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gz.CopyTo(decompressed);
            var snapshot = MessagePackSerializer.Deserialize<ModelBoneCatalogSnapshot>(decompressed.ToArray());
            return snapshot.SchemaVersion != ModelBoneCatalogSnapshot.CurrentSchemaVersion ? null : snapshot;
        }
        catch
        {
            return null;
        }
    }
}
