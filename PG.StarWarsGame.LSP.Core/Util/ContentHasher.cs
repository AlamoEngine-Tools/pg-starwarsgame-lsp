// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Util;

/// <summary>
///     The single content-hash function shared by the game index (unchanged-content fast path),
///     the document text source, and the parse cache. FNV-1a 64-bit over the UTF-16 code units —
///     stable within a session, allocation-free, with ample collision resistance for
///     same-or-changed content gating. Not a persistence format: cache snapshots on disk use
///     <c>ProjectFileHasher</c> instead.
/// </summary>
public static class ContentHasher
{
    public static long Hash(string text)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= prime;
        }

        return unchecked((long)hash);
    }
}
