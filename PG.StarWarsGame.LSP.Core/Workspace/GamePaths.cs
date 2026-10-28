// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     The one rule for a path the game reads: <c>Data\XML\Foo.xml</c> in a file, <c>data/xml/foo.xml</c>
///     in a MEG, <c>DATA/XML/FOO.XML</c> in a registry - the engine resolves them to one file, so the
///     tool compares them as one. Slashes go forward, a relative path carries no outer slash, a
///     dictionary key is lower case, and a comparison ignores case and the slash an author did or did
///     not type (#310). Every path keeps its case except as a key, since a message shows it as written.
/// </summary>
public static class GamePaths
{
    /// <summary>Backslashes to forward slashes, nothing else - for an absolute path or a URI, whose leading slashes mean something.</summary>
    public static string Forward(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>A game-relative path with forward slashes and no leading or trailing slash; case kept.</summary>
    public static string Normalize(string path)
    {
        return Forward(path).Trim('/');
    }

    /// <summary>The form a dictionary keys by: <see cref="Normalize" />d and lower case.</summary>
    public static string Key(string path)
    {
        return Normalize(path).ToLowerInvariant();
    }

    /// <summary>Whether two paths name the same file or folder, whatever their slashes, case or outer slash.</summary>
    public static bool Same(string a, string b)
    {
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Whether <paramref name="path" /> is <paramref name="root" /> or lies inside it, segment by
    ///     segment: <c>Data/XMLx/a.xml</c> is not under <c>Data/XML</c>. An empty root is the game's
    ///     root, which holds everything.
    /// </summary>
    public static bool Under(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (r.Length == 0) return true;
        if (!p.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return false;
        return p.Length == r.Length || p[r.Length] == '/';
    }
}