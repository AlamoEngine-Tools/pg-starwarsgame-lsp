// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>
///     The shared XML parse source: every feature that needs a document's
///     <see cref="ParsedXmlDocument" /> (indexing, diagnostics, hover, inlay hints, completion,
///     code actions, variant resolution) obtains it here instead of re-parsing the text itself.
///     Entries are keyed by canonical URI and validated by content hash, so edits and watcher
///     events invalidate naturally — nothing is wired anywhere.
/// </summary>
/// <remarks>
///     Cached artifacts are read concurrently by request handlers. HtmlAgilityPack is not
///     documented thread-safe, but the HAP 1.12.4 source was verified (2026-07-05): for a document
///     that is never mutated after <c>LoadHtml</c>, <c>InnerHtml</c>/<c>OuterHtml</c> are pure
///     substring reads, <c>InnerText</c> builds into a local StringBuilder, and the remaining lazy
///     members (<c>Name</c>, empty <c>ChildNodes</c>/<c>Attributes</c> collections) are idempotent
///     computations from immutable inputs published by atomic reference stores — release stores
///     under the documented .NET memory model. The traversal stress test in
///     <c>XmlParseCacheTest</c> pins this contract against future HAP upgrades. Consequently,
///     consumers MUST NOT mutate a cached document's DOM.
/// </remarks>
public interface IXmlParseCache
{
    /// <summary>Parse (or reuse) when the caller already holds the current text.</summary>
    ParsedXmlDocument GetOrParse(string canonicalUri, string text);

    /// <summary>
    ///     Parse (or reuse) resolving the text via <see cref="IDocumentTextSource" /> (open buffer
    ///     wins, disk otherwise). Null when the document is neither open nor on disk.
    /// </summary>
    ParsedXmlDocument? GetOrParse(string canonicalUri);
}

public sealed class XmlParseCache : IXmlParseCache
{
    private readonly ParsedDocumentCache<ParsedXmlDocument> _cache;
    private readonly IDocumentTextSource _textSource;

    public XmlParseCache(IDocumentTextSource textSource, int capacity,
        Microsoft.Extensions.Logging.ILogger<XmlParseCache>? logger = null)
    {
        _textSource = textSource;
        _cache = new ParsedDocumentCache<ParsedXmlDocument>(capacity, "XML", logger);
    }

    public (long Hits, long Misses, long Evictions) Statistics => _cache.Statistics;

    public ParsedXmlDocument GetOrParse(string canonicalUri, string text)
    {
        return _cache.GetOrParse(canonicalUri, text, ContentHasher.Hash(text), ParsedXmlDocument.Parse);
    }

    public ParsedXmlDocument? GetOrParse(string canonicalUri)
    {
        var current = _textSource.GetText(canonicalUri);
        return current is null
            ? null
            : _cache.GetOrParse(canonicalUri, current.Text, current.ContentHash, ParsedXmlDocument.Parse);
    }
}
