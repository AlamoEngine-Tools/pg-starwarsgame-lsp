// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

public static class LocalisationContentHash
{
    // FNV-1a 64-bit over the UTF-16 code units - stable within a session, allocation-free, ample
    // collision resistance for the "did this file change on disk since it was last fetched"
    // concurrency guard. Mirrors GameIndexService.ComputeContentHash's algorithm.
    public static string Compute(string text)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash.ToString("x16");
    }
}