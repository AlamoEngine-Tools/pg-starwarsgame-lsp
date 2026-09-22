// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     Resolving one project path against another, on the forward-slash form both resolvers use.
/// </summary>
/// <remarks>
///     <para>
///         A <c>.pgproj</c> names its dependencies by relative path, and two places have to follow
///         those names: the resolver that loads a referenced project, and the graph that orders
///         them. Both had their own copy of the same segment walk.
///     </para>
///     <para>
///         Deliberately not <see cref="Path.GetFullPath(string)" />. These paths are resolved
///         against a recorded directory string rather than the process's current directory, they
///         have to behave the same on Linux and Windows for the same input, and a path naming a
///         file that does not exist yet still has to resolve. What is shared here is the walk
///         alone - the callers keep their own rules about rooted paths and case, because those
///         genuinely differ and are not this function's business.
///     </para>
/// </remarks>
internal static class ProjectPathResolution
{
    /// <summary>The directory part of a normalized path, or empty when it names no directory.</summary>
    public static string GetDirectory(string normalizedPath)
    {
        var idx = normalizedPath.LastIndexOf('/');
        return idx < 0 ? string.Empty : normalizedPath[..idx];
    }

    /// <summary>
    ///     Walks <paramref name="candidate" />'s segments onto <paramref name="directory" />,
    ///     folding <c>.</c> and <c>..</c>.
    /// </summary>
    /// <remarks>
    ///     A <c>..</c> with nothing left to pop is dropped rather than escaping above the base, so a
    ///     path with more <c>..</c> than depth lands at the root instead of walking out of the
    ///     workspace. Both callers relied on that; it is preserved rather than corrected, because
    ///     the alternative is a resolver that can reach arbitrary files from a crafted project file.
    /// </remarks>
    /// <param name="directory">The base, already in forward-slash form. Empty means "here".</param>
    /// <param name="candidate">The path to resolve, already in forward-slash form.</param>
    public static string Resolve(string directory, string candidate)
    {
        var basePath = string.IsNullOrEmpty(directory) ? "." : directory;
        var segments = new List<string>(basePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var basePrefix = basePath.StartsWith('/') ? "/" : string.Empty;

        foreach (var segment in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    if (segments.Count > 0)
                        segments.RemoveAt(segments.Count - 1);
                    break;
                default:
                    segments.Add(segment);
                    break;
            }

        return basePrefix + string.Join('/', segments);
    }

    /// <summary>A POSIX root, or a Windows drive letter.</summary>
    public static bool IsRooted(string path)
    {
        if (path.StartsWith('/'))
            return true;
        return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
    }
}
