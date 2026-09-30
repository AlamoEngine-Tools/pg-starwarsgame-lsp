// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     Names everything a layer's documents read from OUTSIDE their own layer, so a persisted
///     index can be kept when a dependency changed in a way that cannot affect it.
/// </summary>
/// <remarks>
///     <para>
///         Replaces the dependency-<c>OverallHash</c> key, which asked the far coarser question
///         "did the dependency change at all" and discarded a whole dependent layer for one
///         changed line. On EaWX that meant an edit in Core re-parsed all four leaves.
///     </para>
///     <para>
///         Two inputs, VERIFIED against the parsers on 2026-09-30 rather than assumed:
///     </para>
///     <list type="number">
///         <item>
///             The <b>file-type registration</b> of each of this layer's own files. Metafiles in
///             ANY layer register types for a file, and the registered types decide which symbol
///             passes run - so a dependency's metafile genuinely changes how a leaf's file parses.
///             Only THIS layer's files are folded in, which is what makes a dependency's own
///             content edits invisible here.
///         </item>
///         <item>
///             The <b>xml directory set</b>. A story manifest or thread file is indexed as a
///             workspace-file symbol keyed by its path relative to the LONGEST matching xml root,
///             and any layer can contribute a root - so a dependency adding a nested xml directory
///             silently re-keys a leaf's symbols. Directory sets change only when a
///             <c>.pgproj</c> does, so folding the whole union in costs nothing in practice.
///         </item>
///     </list>
///     <para>
///         Everything else the parsers touch is either covered elsewhere or carries no project
///         state: the schema and the story feature flag fold into <see cref="SchemaFingerprint" />;
///         the file helper, parse caches and loggers do not vary by layer.
///     </para>
///     <para>
///         <b>The hazard.</b> A stale index fails SILENTLY - it replays an old parse rather than
///         erroring - and this key is only as good as that list. A parser that gains a new
///         injected input which varies by layer must fold it in here, or bump
///         <see cref="ProjectIndexSnapshot.CurrentSchemaVersion" />.
///     </para>
/// </remarks>
public static class CrossLayerInputFingerprint
{
    /// <summary>
    ///     Computes the key for one layer. <paramref name="layerFileUris" /> are that layer's own
    ///     parseable files; <paramref name="xmlDirectories" /> is the workspace-wide union.
    /// </summary>
    public static string Compute(
        IFileTypeRegistry fileTypeRegistry,
        IReadOnlyList<string> layerFileUris,
        IReadOnlyList<string> xmlDirectories)
    {
        var entries = new List<string>(layerFileUris.Count);

        foreach (var uri in layerFileUris)
        {
            var types = fileTypeRegistry.GetTypesForFile(uri);

            // Folded, like every other lookup keyed by a document path: the same file may be
            // spelled in a different case by the snapshot, this scan, or the host, and a
            // case-sensitive key would miss and re-parse the whole layer on every start. Lowering
            // here is safe where storing a lowered path would not be - this string is hashed and
            // discarded, never handed back to the filesystem.
            var sb = new StringBuilder(uri.ToLowerInvariant());

            // Sorted: the registry makes no ordering promise, and two orders of the same types
            // mean the same thing to the parser.
            foreach (var type in types.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                sb.Append('|').Append(type);

            entries.Add(sb.ToString());
        }

        entries.Sort(StringComparer.Ordinal);

        var all = new StringBuilder();
        foreach (var entry in entries)
            all.Append("f:").Append(entry).Append('\n');

        foreach (var dir in xmlDirectories
                     .Select(d => d.ToLowerInvariant())
                     .OrderBy(d => d, StringComparer.Ordinal))
            all.Append("x:").Append(dir).Append('\n');

        return ContentHasher.Hash(all.ToString()).ToString("x16");
    }
}