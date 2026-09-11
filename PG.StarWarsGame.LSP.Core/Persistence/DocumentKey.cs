// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Security.Cryptography;
using System.Text;

namespace PG.StarWarsGame.LSP.Core.Persistence;

/// <summary>
///     How a persisted document names a file: project-relative, folded the way the game folds, then
///     hashed to a name-based UUID.
///     <para>
///         Three steps, each removing a different kind of leak. Relative to the <c>.pgproj</c>,
///         because an absolute path carries a drive letter and a home directory and so survives
///         neither a clone nor a teammate. Folded, because the engine is case-insensitive and both
///         <c>/</c> and <c>\</c> are separators to it - so a key must agree with the game rather
///         than with the host filesystem. Hashed, because nothing then written to disk carries the
///         shape of anybody's mod tree.
///     </para>
///     <para>
///         What the hash does NOT do is make a path unrecoverable: anyone holding a candidate path
///         can recompute its key. It keeps paths out of the files we write and out of anything we
///         report; it is not a secrecy guarantee. Keep the folded path in memory beside the key so
///         diagnostics stay readable, and let only the key cross a persistence boundary.
///     </para>
///     <para>
///         Resolving a key or a relative path back to a file that opens on a case-sensitive
///         filesystem is a SEPARATE concern, and one this does not attempt:
///         <c>PG.StarWarsGame.Engine.FileSystem</c> owns it. That library answers "are these the
///         same path" and "does this exist", not "what is the canonical form of this path", which is
///         why the fold below is stated here rather than borrowed from it. The rule it states is
///         the same one: <c>PetroglyphFileSystem.PathCharEqual</c> compares with
///         <c>char.ToUpperInvariant</c> and treats either separator as a match.
///     </para>
/// </summary>
public static class DocumentKey
{
    /// <summary>
    ///     The namespace every aet-eaw-edit document key is derived in. Fixed forever: changing it
    ///     re-keys every file anybody has, which is a migration, not a tweak.
    /// </summary>
    public static readonly Guid Namespace = Guid.Parse("6f4d1b3a-4f2a-5c6d-9a1e-2b7c8d9e0f11");

    /// <summary>
    ///     The canonical form of a project-relative path: forward slashes, no leading separator or
    ///     <c>./</c>, no surrounding whitespace, invariant uppercase.
    /// </summary>
    public static string Fold(string relativePath)
    {
        var forward = relativePath.Trim().Replace('\\', '/');
        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];
        forward = forward.TrimStart('/').TrimEnd('/');
        return forward.ToUpperInvariant();
    }

    /// <summary>The key for a project-relative path.</summary>
    public static Guid Of(string relativePath)
    {
        return NameBased(Namespace, Fold(relativePath));
    }

    /// <summary>
    ///     The key for an identity made of several parts - a thread and an event, a campaign and a
    ///     faction.
    ///     <para>
    ///         Hashed WHOLE rather than kept as a key beside the remaining parts in the clear. The
    ///         old sidecars named things by concatenating their parts; keeping half of that
    ///         concatenation readable next to a hash of the other half would leak the readable half
    ///         while looking like it did not.
    ///     </para>
    ///     <para>
    ///         Each part is length-prefixed before joining, so no arrangement of contents can make
    ///         two different identities read as one: "ab" + "c" and "a" + "bc" are different keys
    ///         however the separator is chosen, because the lengths differ.
    ///     </para>
    ///     <para>
    ///         Every part is folded, which is what preserves the case-insensitive equality the
    ///         sidecars matched their parts by.
    ///     </para>
    /// </summary>
    public static Guid Composite(params string[] parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            var folded = Fold(part);
            builder.Append(folded.Length).Append(':').Append(folded).Append('|');
        }

        return NameBased(Namespace, builder.ToString());
    }

    /// <summary>
    ///     Makes <paramref name="absolutePathOrUri" /> relative to <paramref name="projectDirectory" />,
    ///     or null when it lies outside the project and therefore has no portable name.
    /// </summary>
    /// <remarks>
    ///     Accepts a <c>file:///</c> URI because that is the form the server holds documents in.
    ///     The comparison folds case for the same reason the key does - and the URIs it is given
    ///     are lowercased already, which the fold makes harmless.
    /// </remarks>
    public static string? Relative(string projectDirectory, string absolutePathOrUri)
    {
        var path = StripScheme(absolutePathOrUri).Trim().Replace('\\', '/').TrimEnd('/');
        var root = StripScheme(projectDirectory).Trim().Replace('\\', '/').TrimEnd('/') + "/";

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        return path[root.Length..];
    }

    /// <summary>
    ///     RFC 4122 name-based (version 5) UUID: SHA-1 over the namespace laid out big-endian
    ///     followed by the name, with the version and variant bits stamped in.
    /// </summary>
    /// <remarks>
    ///     Written out rather than hand-rolled around <see cref="Guid.NewGuid" /> or a plain hash
    ///     because the whole value of the key is that two machines, two runtimes and two releases
    ///     derive the same one. Only a specified algorithm promises that; the RFC test vector in
    ///     <c>DocumentKeyTest</c> is what holds us to it.
    /// </remarks>
    public static Guid NameBased(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray(true); // big-endian, as the RFC lays it out
        var nameBytes = Encoding.UTF8.GetBytes(name);

        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        var hash = SHA1.HashData(input);

        var key = new byte[16];
        Array.Copy(hash, key, 16);
        key[6] = (byte)((key[6] & 0x0F) | 0x50); // version 5
        key[8] = (byte)((key[8] & 0x3F) | 0x80); // RFC 4122 variant

        return new Guid(key, true);
    }

    private static string StripScheme(string pathOrUri)
    {
        const string scheme = "file://";
        if (!pathOrUri.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return pathOrUri;

        var rest = pathOrUri[scheme.Length..];
        // file:///c:/... keeps the drive letter; file:///home/... keeps its leading slash.
        return rest.Length > 2 && rest[0] == '/' && char.IsLetter(rest[1]) && rest[2] == ':'
            ? rest[1..]
            : rest;
    }
}
