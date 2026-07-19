// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Parsing;

/// <summary>
///     A Lua document's text with its Loretta parse - the two always travel together so consumers
///     never have to reverse the text out of the tree. Mirrors <c>ParsedXmlDocument</c> on the XML
///     side. Loretta trees are Roslyn-style immutable red/green trees and are safe for concurrent
///     readers by design.
/// </summary>
public sealed class ParsedLuaDocument
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private ParsedLuaDocument(string text, SyntaxTree tree)
    {
        Text = text;
        Tree = tree;
    }

    public string Text { get; }

    public SyntaxTree Tree { get; }

    public static ParsedLuaDocument Parse(string text, string path = "")
    {
        return new ParsedLuaDocument(text, LuaSyntaxTree.ParseText(text, s_parseOptions, path));
    }
}

/// <summary>
///     The shared Lua parse source: indexing, diagnostics (which used to parse the same text four
///     times per publish), and every request handler obtain a document's
///     <see cref="ParsedLuaDocument" /> here instead of re-parsing. Entries are keyed by canonical
///     URI and validated by content hash, so edits and watcher events invalidate naturally.
/// </summary>
public interface ILuaParseCache
{
    /// <summary>Parse (or reuse) when the caller already holds the current text.</summary>
    ParsedLuaDocument GetOrParse(string canonicalUri, string text);

    /// <summary>
    ///     Parse (or reuse) resolving the text via <see cref="IDocumentTextSource" /> (open buffer
    ///     wins, disk otherwise). Null when the document is neither open nor on disk.
    /// </summary>
    ParsedLuaDocument? GetOrParse(string canonicalUri);
}

public sealed class LuaParseCache : ILuaParseCache
{
    private readonly ParsedDocumentCache<ParsedLuaDocument> _cache;
    private readonly IDocumentTextSource _textSource;

    public LuaParseCache(IDocumentTextSource textSource, int capacity,
        ILogger<LuaParseCache>? logger = null)
    {
        _textSource = textSource;
        _cache = new ParsedDocumentCache<ParsedLuaDocument>(capacity, "Lua", logger);
    }

    public (long Hits, long Misses, long Evictions) Statistics => _cache.Statistics;

    public ParsedLuaDocument GetOrParse(string canonicalUri, string text)
    {
        return _cache.GetOrParse(canonicalUri, text, ContentHasher.Hash(text),
            t => ParsedLuaDocument.Parse(t, canonicalUri));
    }

    public ParsedLuaDocument? GetOrParse(string canonicalUri)
    {
        var current = _textSource.GetText(canonicalUri);
        return current is null
            ? null
            : _cache.GetOrParse(canonicalUri, current.Text, current.ContentHash,
                t => ParsedLuaDocument.Parse(t, canonicalUri));
    }
}