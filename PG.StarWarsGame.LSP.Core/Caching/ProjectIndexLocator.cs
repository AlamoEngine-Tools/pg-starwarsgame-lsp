// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Caching;

public static class ProjectIndexLocator
{
    /// <summary>How many hex digits of the context key name the snapshot file.</summary>
    private const int KeyDigitsInFileName = 8;

    public static string GetAetswgDirectory(string pgprojPath)
    {
        var dir = GetDirectory(pgprojPath);
        return dir + "/.aetswg";
    }

    /// <summary>
    ///     Where the index snapshot of this layer lives for one context: <c>indices/&lt;stem&gt;.&lt;key&gt;.msgpack</c>.
    /// </summary>
    /// <remarks>
    ///     The context key is the cross-layer fingerprint - what the leaf on top registers for this
    ///     layer's files, and the xml roots that can match them. A dependency opened under two
    ///     different leaves can parse differently under each, so each context keeps its own file
    ///     instead of the two overwriting one (MEASURED 2026-10-04: four re-parses of EaWX core in
    ///     seven starts). Only the first <see cref="KeyDigitsInFileName" /> digits name the file;
    ///     the full key is still checked inside the snapshot, so a collision costs one re-parse,
    ///     never a stale index.
    /// </remarks>
    public static string GetIndexFilePath(string pgprojPath, string contextKey)
    {
        var stem = GetStem(pgprojPath);
        var key = contextKey.Length > KeyDigitsInFileName ? contextKey[..KeyDigitsInFileName] : contextKey;
        return GetIndexDirectory(pgprojPath) + "/" + stem + "." + key + ".msgpack";
    }

    /// <summary>The search pattern matching every context's snapshot of this layer, and no other layer's.</summary>
    public static string GetIndexFilePattern(string pgprojPath)
    {
        return GetStem(pgprojPath) + ".*.msgpack";
    }

    /// <summary>The keyless <c>indices/&lt;stem&gt;.msgpack</c> that versions before the context key wrote.</summary>
    public static string GetLegacyIndexFilePath(string pgprojPath)
    {
        return GetIndexDirectory(pgprojPath) + "/" + GetStem(pgprojPath) + ".msgpack";
    }

    public static string GetIndexDirectory(string pgprojPath)
    {
        return GetAetswgDirectory(pgprojPath) + "/indices";
    }

    /// <summary>
    ///     Where this project's persisted model-bone catalog lives. A sibling of
    ///     <c>indices/</c> rather than a file inside it, so the two are invalidated independently -
    ///     an XML edit must not throw away a 25-second ALO extraction.
    /// </summary>
    public static string GetModelBonesFilePath(string pgprojPath)
    {
        var stem = GetStem(pgprojPath);
        return GetAetswgDirectory(pgprojPath) + "/bones/" + stem + ".msgpack";
    }

    private static string GetDirectory(string path)
    {
        var normalized = path.Replace('\\', '/');
        var idx = normalized.LastIndexOf('/');
        return idx < 0 ? "." : normalized[..idx];
    }

    private static string GetStem(string path)
    {
        var normalized = path.Replace('\\', '/');
        var idx = normalized.LastIndexOf('/');
        var filename = idx < 0 ? normalized : normalized[(idx + 1)..];
        var dotIdx = filename.LastIndexOf('.');
        return dotIdx < 0 ? filename : filename[..dotIdx];
    }
}