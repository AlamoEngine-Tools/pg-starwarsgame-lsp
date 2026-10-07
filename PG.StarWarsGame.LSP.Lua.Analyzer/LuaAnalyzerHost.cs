// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Lua.Analyzer;

/// <summary>
///     Runs emmylua_ls as a child process and is its LSP client.
/// </summary>
/// <remarks>
///     <para>
///         The editor never talks to the analyzer: this server mirrors the open documents to it,
///         asks it what it needs, and decides what reaches the editor. So the analyzer is a
///         dependency that can be missing or die, and every call here is safe either way - a
///         request answers with nothing, a document change is kept for the next process.
///     </para>
///     <para>
///         A process that dies is restarted after a delay that doubles with every crash in a row,
///         and is told about every open document again; after
///         <see cref="LuaAnalyzerOptions.MaxConsecutiveCrashes" /> in a row it stays off.
///     </para>
/// </remarks>
public sealed class LuaAnalyzerHost : ILuaAnalyzer, IAsyncDisposable, IDisposable
{
    private const string ConfigFileName = "emmyrc.json";

    private readonly Dictionary<string, (string Text, int Version)> _documents = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ILogger<LuaAnalyzerHost> _logger;
    private readonly LuaAnalyzerOptions _options;
    private int _consecutiveCrashes;
    private volatile bool _disposed;
    private AnalyzerPlan? _plan;
    private volatile Session? _session;

    public LuaAnalyzerHost(LuaAnalyzerOptions options, ILogger<LuaAnalyzerHost> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>How many analyzer processes have been started and initialized.</summary>
    public int Starts { get; private set; }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _lifecycle.WaitAsync();
        try
        {
            await StopSessionAsync();
        }
        finally
        {
            _lifecycle.Release();
        }

        DeleteConfigDirectory();
    }

    /// <summary>
    ///     The container's shutdown path: no handshake, the process is ended. A server going away
    ///     must never leave an analyzer running.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        if (_session is { } session)
        {
            _session = null;
            Kill(session.Process);
            session.Client.Dispose();
            session.Process.Dispose();
        }

        DeleteConfigDirectory();
    }

    private void DeleteConfigDirectory()
    {
        try
        {
            if (Directory.Exists(_options.ConfigDirectory)) Directory.Delete(_options.ConfigDirectory, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Lua analyzer configuration directory not removed");
        }
    }

    public bool IsRunning => !_disposed && _session is not null;

    public event Action<PublishDiagnosticsParams>? DiagnosticsPublished;

    public void DidOpen(string uri, string text, int version)
    {
        lock (_documents)
        {
            _documents[uri] = (text, version);
        }

        _session?.Client.DidOpenTextDocument(OpenParams(uri, text, version));
    }

    public void DidChange(string uri, string text, int version)
    {
        lock (_documents)
        {
            _documents[uri] = (text, version);
        }

        _session?.Client.DidChangeTextDocument(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
                { Uri = DocumentUri.From(uri), Version = version },
            ContentChanges =
                new Container<TextDocumentContentChangeEvent>(new TextDocumentContentChangeEvent { Text = text })
        });
    }

    public void DidClose(string uri)
    {
        lock (_documents)
        {
            _documents.Remove(uri);
        }

        _session?.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(uri))
        });
    }

    /// <summary>
    ///     Starts the analyzer for <paramref name="plan" />, replacing a running one. Nothing starts
    ///     when the leaf has no scripts or the executable cannot be launched.
    /// </summary>
    public async Task StartAsync(AnalyzerPlan plan)
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_disposed) return;
            _plan = plan;
            _consecutiveCrashes = 0;
            await StopSessionAsync();
            await StartSessionAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    ///     Sends a request to the analyzer. Null when it is not running, does not answer within
    ///     <paramref name="timeout" /> (or the configured default), or fails.
    /// </summary>
    public async Task<T?> RequestAsync<T>(Func<ILanguageClient, CancellationToken, Task<T>> request,
        TimeSpan? timeout = null, CancellationToken ct = default) where T : class
    {
        if (_session is not { } session || _disposed) return null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? _options.RequestTimeout);
        try
        {
            return await request(session.Client, cts.Token).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Lua analyzer request timed out after {Timeout}", timeout ?? _options.RequestTimeout);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "Lua analyzer request failed");
            return null;
        }
    }

    public Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken ct) where T : class
    {
        return RequestAsync((client, token) => client.SendRequest(method, parameters).Returning<T>(token), ct: ct);
    }

    private async Task StartSessionAsync()
    {
        if (_plan is not { WorkspaceRoots.Count: > 0 } plan)
        {
            _logger.LogInformation("Lua analyzer not started: the project has no script roots");
            return;
        }

        Directory.CreateDirectory(_options.ConfigDirectory);
        var configPath = Path.Combine(_options.ConfigDirectory, ConfigFileName);
        await File.WriteAllTextAsync(configPath, plan.ToJson());

        var startInfo = new ProcessStartInfo(_options.Command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in _options.LeadingArguments.Concat(LuaAnalyzerOptions.AnalyzerArguments))
            startInfo.ArgumentList.Add(arg);
        startInfo.Environment["EMMYLUALS_CONFIG"] = configPath;
        foreach (var (key, value) in _options.Environment) startInfo.Environment[key] = value;

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning("Lua analyzer not started: {Command} could not be launched ({Reason})",
                _options.Command, ex.Message);
            return;
        }

        _ = DrainAsync(process.StandardError);
        LanguageClient client;
        try
        {
            using var cts = new CancellationTokenSource(_options.StartTimeout);
            client = await LanguageClient.From(options =>
            {
                options.Input = PipeReader.Create(process.StandardOutput.BaseStream);
                options.Output = PipeWriter.Create(process.StandardInput.BaseStream);
                options.RootUri = DocumentUri.FromFileSystemPath(plan.WorkspaceRoots[0]);
                foreach (var root in plan.WorkspaceRoots)
                    options.WithWorkspaceFolder(new WorkspaceFolder
                    {
                        Uri = DocumentUri.FromFileSystemPath(root), Name = Path.GetFileName(root.TrimEnd('/', '\\'))
                    });
                options.OnPublishDiagnostics(p => DiagnosticsPublished?.Invoke(p));
                // The analyzer asks for its 'emmylua' and 'files' sections; its configuration is the
                // file, so every section answers null - and an unanswered request stalls it.
                options.OnRequest("workspace/configuration",
                    (ConfigurationParams p) => Task.FromResult(new JArray(p.Items.Select(_ => JValue.CreateNull()))));
            }, cts.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Lua analyzer did not initialize; stopping it");
            Kill(process);
            return;
        }

        var session = new Session(process, client, DateTime.UtcNow);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _ = OnExitedAsync(session);
        _session = session;
        Starts++;
        _logger.LogInformation("Lua analyzer started (pid {Pid}) for {Roots}", process.Id,
            string.Join(", ", plan.WorkspaceRoots));

        List<KeyValuePair<string, (string Text, int Version)>> open;
        lock (_documents)
        {
            open = _documents.ToList();
        }

        foreach (var (uri, (text, version)) in open) client.DidOpenTextDocument(OpenParams(uri, text, version));
        if (process.HasExited) _ = OnExitedAsync(session);
    }

    private async Task OnExitedAsync(Session session)
    {
        if (_disposed || !ReferenceEquals(session, _session)) return;

        var uptime = DateTime.UtcNow - session.StartedAt;
        await _lifecycle.WaitAsync();
        try
        {
            if (_disposed || !ReferenceEquals(session, _session)) return;
            _session = null;
            session.Client.Dispose();
            if (uptime > _options.StableAfter) _consecutiveCrashes = 0;
            _consecutiveCrashes++;
            if (_consecutiveCrashes > _options.MaxConsecutiveCrashes)
            {
                _logger.LogError("Lua analyzer exited {Count} times in a row; it stays off for this session",
                    _consecutiveCrashes - 1);
                return;
            }
        }
        finally
        {
            _lifecycle.Release();
        }

        var delay = TimeSpan.FromTicks(Math.Min(_options.MaxRestartDelay.Ticks,
            _options.RestartDelay.Ticks * (1L << Math.Min(_consecutiveCrashes - 1, 20))));
        _logger.LogWarning("Lua analyzer exited after {Uptime}; restarting in {Delay}", uptime, delay);
        await Task.Delay(delay);

        await _lifecycle.WaitAsync();
        try
        {
            if (_disposed || _session is not null) return;
            await StartSessionAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StopSessionAsync()
    {
        if (_session is not { } session) return;
        _session = null;
        try
        {
            await session.Client.Shutdown().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "Lua analyzer did not shut down cleanly");
        }

        if (!session.Process.WaitForExit(TimeSpan.FromSeconds(2))) Kill(session.Process);
        session.Client.Dispose();
        session.Process.Dispose();
    }

    private void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            _logger.LogDebug(ex, "Lua analyzer process already gone");
        }
    }

    private async Task DrainAsync(StreamReader stderr)
    {
        try
        {
            while (await stderr.ReadLineAsync() is { } line) _logger.LogDebug("[emmylua] {Line}", line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // the process is gone
        }
    }

    private static DidOpenTextDocumentParams OpenParams(string uri, string text, int version)
    {
        return new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = DocumentUri.From(uri), LanguageId = "lua", Version = version, Text = text
            }
        };
    }

    private sealed record Session(Process Process, LanguageClient Client, DateTime StartedAt);
}