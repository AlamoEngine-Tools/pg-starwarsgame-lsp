// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Text;
using Loretta.CodeAnalysis.Lua;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Generated;

/// <summary>
///     The text the analyzer reads for a document: the author's, with the annotation lines this server
///     knows inserted above its functions (#251), and the line map that carries positions across.
///     One per server - a file is generated the same way whichever script instance holds it, so its
///     map is one too.
/// </summary>
public sealed class LuaGeneratedTexts(IEnumerable<ILuaAnnotationSource> sources, IFileHelper fileHelper)
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly ConcurrentDictionary<string, LuaLineMap> _maps = new(DocumentUris.Comparer);
    private readonly IReadOnlyList<ILuaAnnotationSource> _sources = sources.ToList();

    /// <summary>Whether any document has insertions.</summary>
    public bool IsEmpty => _maps.IsEmpty;

    /// <summary>The generated text of <paramref name="uri" />; the text itself when nothing is inserted.</summary>
    public string Generate(string uri, string text)
    {
        var key = fileHelper.NormalizeUri(uri);
        IReadOnlyList<LuaAnnotationInsertion> insertions;
        try
        {
            var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
            insertions = _sources.SelectMany(s => s.For(tree, key)).ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            insertions = [];
        }

        if (insertions.Count == 0)
        {
            _maps.TryRemove(key, out _);
            return text;
        }

        _maps[key] = new LuaLineMap(insertions);
        var eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var byLine = insertions.GroupBy(i => i.BeforeLine).ToDictionary(g => g.Key, g => g.SelectMany(i => i.Lines));
        var source = LuaSyntaxTree.ParseText(text, s_parseOptions).GetText();
        var generated = new StringBuilder(text.Length + insertions.Count * 64);
        for (var i = 0; i < source.Lines.Count; i++)
        {
            if (byLine.TryGetValue(i, out var lines))
                foreach (var line in lines)
                    generated.Append(line).Append(eol);
            var span = source.Lines[i].SpanIncludingLineBreak;
            generated.Append(text, span.Start, span.Length);
        }

        return generated.ToString();
    }

    /// <summary>The line map of <paramref name="uri" />, or null when its text went to the analyzer as it is.</summary>
    public LuaLineMap? MapOf(string? uri)
    {
        return uri is not null && _maps.TryGetValue(fileHelper.NormalizeUri(uri), out var map) ? map : null;
    }

    /// <summary>Drops the map of a document no instance holds any more.</summary>
    public void Forget(string uri)
    {
        _maps.TryRemove(fileHelper.NormalizeUri(uri), out _);
    }
}
