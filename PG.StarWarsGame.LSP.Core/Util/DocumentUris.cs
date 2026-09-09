// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Util;

/// <summary>
///     How two normalized document URIs are compared, and the comparer every collection keyed by one
///     must be built with.
/// </summary>
/// <remarks>
///     <para>
///         The engine is case-insensitive: <c>FOO.xml</c> and <c>foo.xml</c> are the same asset by
///         design, and our idea of "the same document" has to agree with the game rather than with
///         the host filesystem. That much was always true - what changed is WHERE the fold happens.
///     </para>
///     <para>
///         It used to be baked into the value: <see cref="IFileHelper.PathToFileUri" /> lowercased
///         every path, so a URI could never be turned back into a file that opens on a case-sensitive
///         filesystem. Folding is a property of the COMPARISON, not of the data; the value now keeps
///         the case it was given and this comparer supplies the rest.
///     </para>
///     <para>
///         Ordinal-ignore-case is the whole rule, because separators are already unified by
///         <see cref="IFileHelper.NormalizeUri" /> before anything reaches here. Never compare
///         normalized URIs with <c>StringComparison.Ordinal</c>, and never build a dictionary or set
///         of them without this comparer: both are lookups that silently miss rather than fail.
///     </para>
/// </remarks>
public static class DocumentUris
{
    /// <summary>The comparer for normalized document URIs, and for dictionaries keyed by one.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Whether two already-normalized URIs name the same document.</summary>
    public static bool Same(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a normalized URI sits under a normalized directory URI or path prefix.</summary>
    /// <remarks>
    ///     Prefix tests over URIs used to work by accident, because both sides were lowercased on the
    ///     way in. They are spelled out here so the fold stays with the comparison.
    /// </remarks>
    public static bool StartsWith(string uri, string prefix)
    {
        return uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a normalized URI contains a segment - <c>"/data/xml/ai/"</c> and the like.</summary>
    public static bool Contains(string uri, string fragment)
    {
        return uri.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    }
}
