// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Instances;

/// <summary>
///     Which analyzer answers: every answer about a file comes from the states holding it.
/// </summary>
/// <remarks>
///     <para>
///         A script nobody requires is held by its own instance alone, so the per-script analyzers
///         (<see cref="LuaAnalyzerInstances" />) answer everything about it. A library is held by every
///         script requiring it: where it is used - definitions, references, lenses - is downstream, so
///         the whole-project analyzer answers that, and the handlers keep only the states holding the
///         library (#249). A rename is the whole-project analyzer's from any file, under the rename
///         scope the handlers apply. Findings, hover, completion, hints and tokens are the instances'
///         alone: the whole-project analyzer sees one namespace, so nothing it types reaches anything.
///     </para>
///     <para>
///         Both are told of every document; only the instances' findings are published.
///     </para>
/// </remarks>
public sealed class LuaAnalyzerRoutes : ILuaAnalyzer
{
    // A lens the whole-project analyzer made carries this around its own data, so its resolve goes back.
    private const string ProjectLens = "aet.project";
    private const string Resolve = "codeLens/resolve";
    private const string CodeLens = "textDocument/codeLens";
    private static readonly JsonSerializer s_json = new LspSerializer().JsonSerializer;

    private static readonly HashSet<string> s_downstream = new(StringComparer.Ordinal)
    {
        "textDocument/definition", "textDocument/references", CodeLens
    };

    private static readonly HashSet<string> s_rename = new(StringComparer.Ordinal)
    {
        "textDocument/rename", "textDocument/prepareRename"
    };

    private readonly ILuaAnalyzer _instances;
    private readonly Func<string, bool> _isShared;
    private readonly ILuaAnalyzer _project;

    /// <param name="instances">The per-script analyzers.</param>
    /// <param name="project">The whole-project analyzer.</param>
    /// <param name="isShared">Whether a script other than the file itself requires the file.</param>
    public LuaAnalyzerRoutes(ILuaAnalyzer instances, ILuaAnalyzer project, Func<string, bool> isShared)
    {
        _instances = instances;
        _project = project;
        _isShared = isShared;
        _instances.DiagnosticsPublished += published => DiagnosticsPublished?.Invoke(published);
    }

    public bool IsRunning => _instances.IsRunning;

    public event Action<PublishDiagnosticsParams>? DiagnosticsPublished;

    public void DidOpen(string uri, string text, int version)
    {
        _instances.DidOpen(uri, text, version);
        _project.DidOpen(uri, text, version);
    }

    public void DidChange(string uri, string text, int version)
    {
        _instances.DidChange(uri, text, version);
        _project.DidChange(uri, text, version);
    }

    public void DidClose(string uri)
    {
        _instances.DidClose(uri);
        _project.DidClose(uri);
    }

    public async Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken ct) where T : class?
    {
        if (method == Resolve)
        {
            var lens = JToken.FromObject(parameters, s_json);
            if (lens["data"] is not JObject { } data || data[ProjectLens] is not { } own)
                return await _instances.RequestAsync<T>(method, parameters, ct);
            lens["data"] = own;
            var resolved = await _project.RequestAsync<JToken>(method, lens, ct);
            if (resolved is JObject resolvedLens) resolvedLens["data"] = data;
            return resolved?.ToObject<T>(s_json);
        }

        var origin = JToken.FromObject(parameters, s_json)["textDocument"]?["uri"]?.Value<string>();
        var toProject = s_rename.Contains(method) ||
                        (s_downstream.Contains(method) && origin is not null && _isShared(origin));
        if (!toProject) return await _instances.RequestAsync<T>(method, parameters, ct);
        if (method != CodeLens) return await _project.RequestAsync<T>(method, parameters, ct);

        var lenses = await _project.RequestAsync<JToken>(method, parameters, ct);
        if (lenses is JArray array)
            foreach (var item in array.OfType<JObject>())
                item["data"] = new JObject { [ProjectLens] = item["data"] ?? JValue.CreateNull() };
        return lenses?.ToObject<T>(s_json);
    }
}
