// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Generated;

/// <summary>
///     The analyzer, fed generated documents (#251). It takes types only from annotations in the
///     script itself, so what this server knows - a stub's signature, a state handler's message - is
///     written above the script's functions in the text the analyzer gets, never in the author's.
/// </summary>
/// <remarks>
///     Only whole lines are inserted, so a <see cref="LuaLineMap" /> per document carries every
///     position across: requests onto the generated document, answers and diagnostics off it. A
///     result on a generated line is dropped; an edit touching one is refused whole, since it would
///     write into text the author never wrote. A document without insertions passes through as it is.
/// </remarks>
public sealed class GeneratedAnalyzerDocuments : ILuaAnalyzer
{
    private static readonly JsonSerializer s_json = new LspSerializer().JsonSerializer;

    // Answers that are edits: refused whole when any part lands on a generated line.
    private static readonly HashSet<string> s_edits = new(StringComparer.Ordinal)
    {
        "textDocument/rename", "textDocument/formatting", "textDocument/rangeFormatting", "textDocument/codeAction"
    };

    private readonly ILuaAnalyzer _inner;
    private readonly LuaGeneratedTexts _texts;

    public GeneratedAnalyzerDocuments(ILuaAnalyzer inner, IEnumerable<ILuaAnnotationSource> sources,
        IFileHelper fileHelper) : this(inner, new LuaGeneratedTexts(sources, fileHelper))
    {
    }

    /// <param name="inner">The analyzer behind.</param>
    /// <param name="texts">
    ///     The generated texts, shared with whatever sends the analyzer documents of its own - the files
    ///     a script requires, read from disk.
    /// </param>
    public GeneratedAnalyzerDocuments(ILuaAnalyzer inner, LuaGeneratedTexts texts)
    {
        _inner = inner;
        _texts = texts;
        _inner.DiagnosticsPublished += OnDiagnostics;
    }

    public bool IsRunning => _inner.IsRunning;

    public event Action<PublishDiagnosticsParams>? DiagnosticsPublished;

    public void DidOpen(string uri, string text, int version)
    {
        _inner.DidOpen(uri, _texts.Generate(uri, text), version);
    }

    public void DidChange(string uri, string text, int version)
    {
        _inner.DidChange(uri, _texts.Generate(uri, text), version);
    }

    public void DidClose(string uri)
    {
        _texts.Forget(uri);
        _inner.DidClose(uri);
    }

    public async Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken ct) where T : class?
    {
        if (_texts.IsEmpty) return await _inner.RequestAsync<T>(method, parameters, ct);

        var request = JToken.FromObject(parameters, s_json);
        var origin = OriginOf(request);
        Walk(request, origin, origin, (map, line) => map.ToGenerated(line), new Hits());

        var answer = await _inner.RequestAsync<JToken>(method, request, ct);
        if (answer is null || answer.Type == JTokenType.Null) return null;

        if (method == "textDocument/semanticTokens/full")
            MapSemanticTokens(answer, origin);
        else
        {
            var hits = new Hits();
            var kept = Walk(answer, origin, origin, (map, line) => map.ToAuthored(line), hits);
            if (!kept || (hits.Any && s_edits.Contains(method))) return null;
        }

        return answer.ToObject<T>(s_json);
    }

    // ── mapping ──────────────────────────────────────────────────────────────

    private sealed class Hits
    {
        public bool Any { get; set; }
    }

    // The document a request is about, and the document of every position in its answer that does
    // not name its own.
    private static string? OriginOf(JToken request)
    {
        return request["textDocument"]?["uri"]?.Value<string>() ?? request["data"]?["uri"]?.Value<string>();
    }

    private LuaLineMap? MapOf(string? uri)
    {
        return _texts.MapOf(uri);
    }

    // Maps every position under token in place. False when token itself rests on a generated line;
    // an array drops such elements, and the caller drops whatever returned false at the top.
    private bool Walk(JToken token, string? uri, string? origin, Func<LuaLineMap, int, int?> line, Hits hits)
    {
        switch (token)
        {
            case JArray array:
                for (var i = array.Count - 1; i >= 0; i--)
                    if (!Walk(array[i], uri, origin, line, hits))
                        array.RemoveAt(i);
                return true;
            case JObject obj:
                var own = obj["uri"]?.Type == JTokenType.String ? obj["uri"]!.Value<string>()
                    : obj["targetUri"]?.Type == JTokenType.String ? obj["targetUri"]!.Value<string>()
                    : obj["textDocument"]?["uri"]?.Value<string>();
                var context = own ?? uri;
                if (!MapLines(obj, MapOf(context), line, hits)) return false;
                foreach (var property in obj.Properties().ToList())
                {
                    if (property.Name == "changes" && property.Value is JObject changes)
                    {
                        foreach (var edited in changes.Properties())
                            if (!Walk(edited.Value, edited.Name, origin, line, hits))
                                return false;
                        continue;
                    }

                    var child = property.Name == "originSelectionRange" ? origin : context;
                    if (!Walk(property.Value, child, origin, line, hits)) return false;
                }

                return true;
            default:
                return true;
        }
    }

    // A position ({line, character}) or a folding range ({startLine, endLine}).
    private static bool MapLines(JObject obj, LuaLineMap? map, Func<LuaLineMap, int, int?> line, Hits hits)
    {
        if (map is null) return true;
        foreach (var name in obj.ContainsKey("character") ? ["line"] : new[] { "startLine", "endLine" })
        {
            if (obj[name] is not { Type: JTokenType.Integer } value) continue;
            if (line(map, value.Value<int>()) is not { } mapped)
            {
                hits.Any = true;
                return false;
            }

            obj[name] = mapped;
        }

        return true;
    }

    // Tokens are (deltaLine, deltaStart, length, type, modifiers), each relative to the one before:
    // decoded, those on generated lines dropped, the rest re-encoded on authored lines.
    private void MapSemanticTokens(JToken answer, string? origin)
    {
        if (MapOf(origin) is not { } map || answer["data"] is not JArray data) return;
        var kept = new JArray();
        int line = 0, column = 0, lastLine = 0, lastColumn = 0;
        for (var i = 0; i + 4 < data.Count; i += 5)
        {
            var deltaLine = data[i].Value<int>();
            line += deltaLine;
            column = deltaLine == 0 ? column + data[i + 1].Value<int>() : data[i + 1].Value<int>();
            if (map.ToAuthored(line) is not { } authored) continue;
            kept.Add(authored - lastLine);
            kept.Add(authored == lastLine ? column - lastColumn : column);
            kept.Add(data[i + 2]);
            kept.Add(data[i + 3]);
            kept.Add(data[i + 4]);
            (lastLine, lastColumn) = (authored, column);
        }

        answer["data"] = kept;
    }

    private void OnDiagnostics(PublishDiagnosticsParams published)
    {
        if (MapOf(published.Uri.ToString()) is null)
        {
            DiagnosticsPublished?.Invoke(published);
            return;
        }

        var json = JToken.FromObject(published, s_json);
        Walk(json, published.Uri.ToString(), published.Uri.ToString(), (map, line) => map.ToAuthored(line),
            new Hits());
        DiagnosticsPublished?.Invoke(json.ToObject<PublishDiagnosticsParams>(s_json)!);
    }
}