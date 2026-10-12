// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Lua.Tests;

/// <summary>
///     An analyzer that answers each method with what the test scripted, and records what it was
///     asked. A method with no script answers null - as a stopped or timed-out analyzer does.
/// </summary>
internal sealed class ScriptedLuaAnalyzer : ILuaAnalyzer
{
    private readonly Dictionary<string, object> _answers = new(StringComparer.Ordinal);

    public List<(string Method, object Parameters)> Requests { get; } = [];

    public bool IsRunning { get; set; } = true;

    public event Action<PublishDiagnosticsParams>? DiagnosticsPublished
    {
        add { }
        remove { }
    }

    public void DidOpen(string uri, string text, int version)
    {
    }

    public void DidChange(string uri, string text, int version)
    {
    }

    public void DidClose(string uri)
    {
    }

    public Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken ct) where T : class
    {
        Requests.Add((method, parameters));
        return Task.FromResult(_answers.GetValueOrDefault(method) as T);
    }

    public ScriptedLuaAnalyzer Answer(string method, object answer)
    {
        _answers[method] = answer;
        return this;
    }
}
