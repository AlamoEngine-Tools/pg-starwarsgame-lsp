// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Compression;
using MessagePack;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Caching;

public static class ProjectIndexSerializer
{
    public static byte[] Serialize(ProjectIndexSnapshot snapshot)
    {
        var msgpack = MessagePackSerializer.Serialize(snapshot);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
        {
            gz.Write(msgpack);
        }

        return ms.ToArray();
    }

    public static ProjectIndexSnapshot? Deserialize(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gz.CopyTo(decompressed);
            var snapshot = MessagePackSerializer.Deserialize<ProjectIndexSnapshot>(decompressed.ToArray());
            return snapshot.SchemaVersion != ProjectIndexSnapshot.CurrentSchemaVersion ? null : snapshot;
        }
        catch
        {
            return null;
        }
    }

    public static ProjectFileEntry ToEntry(string relativePath, string contentHash, DocumentIndex doc)
    {
        return new ProjectFileEntry
        {
            RelativePath = relativePath,
            ContentHash = contentHash,
            Document = new SerializedDocument
            {
                Symbols = doc.Symbols.ToArray(),
                References = doc.References.Select(SerializedReference.FromRuntime).ToArray(),
                RequireArgs = doc.RequireArgs.IsDefaultOrEmpty ? [] : doc.RequireArgs.ToArray(),
                GroupMemberships = doc.GroupMemberships.IsDefaultOrEmpty
                    ? []
                    : doc.GroupMemberships.Select(SerializedDocumentGroupMembership.FromRuntime).ToArray(),
                LayerRank = doc.LayerRank,
                LayerName = doc.LayerName
            }
        };
    }

    public static DocumentIndex FromEntry(ProjectFileEntry entry, string absoluteUri)
    {
        var d = entry.Document;
        return new DocumentIndex(
            absoluteUri,
            0,
            d.Symbols.ToImmutableArray(),
            d.References.Select(r => r.ToRuntime()).ToImmutableArray(),
            d.RequireArgs.ToImmutableArray(),
            d.GroupMemberships.Select(gm => gm.ToRuntime()).ToImmutableArray(),
            d.LayerRank,
            d.LayerName
        );
    }
}