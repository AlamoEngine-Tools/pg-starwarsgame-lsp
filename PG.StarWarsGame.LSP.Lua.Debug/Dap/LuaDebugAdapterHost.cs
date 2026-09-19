// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Pipelines;
using AnakinRaW.CommonUtilities.Hashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Server;
using OmniSharp.Extensions.JsonRpc;
using PG.Commons;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <summary>
///     Runs one <see cref="LuaDebugAdapter" /> as a DAP server over a pair of streams. The
///     standalone process (the language server exe started with <c>--debug-adapter</c>) uses
///     <see cref="RunAsync" />; tests wire the same server to in-memory pipes with
///     <see cref="StartAsync" />.
/// </summary>
public static class LuaDebugAdapterHost
{
    /// <summary>
    ///     The container a standalone adapter process needs: the real file system, the Core file
    ///     helper, PG.Commons' hashing (registered exactly once, here), logging and the debugger
    ///     services.
    /// </summary>
    public static IServiceProvider CreateServices(ILoggerFactory loggerFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IFileHelper, FileHelper>();
        // The CRC-32 service resolves IHashingService, which only the newer PG.Commons registers
        // in ContributeServices. Two builds of that library reach the server's output folder (the
        // standalone PetroglyphTools and ModVerify's copy), so the hashing service is supplied here
        // the way the baseline builder does it; under the newer copy this TryAdd is a no-op.
        services.TryAddSingleton<IHashingService>(sp => new HashingService(sp));
        PetroglyphCommons.ContributeServices(services);
        services.AddLuaDebugServices();
        return services.BuildServiceProvider();
    }

    /// <summary>Serves DAP on the given streams until the client disconnects.</summary>
    public static async Task RunAsync(Stream input, Stream output, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var services = CreateServices(loggerFactory);
        var adapter = services.GetRequiredService<LuaDebugAdapter>();
        var server = await StartAsync(PipeReader.Create(input), PipeWriter.Create(output), adapter, loggerFactory,
                cancellationToken)
            .ConfigureAwait(false);
        using (server)
        {
            await adapter.Exited.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Starts a DAP server bound to the adapter; completes once the client's initialize request is answered.</summary>
    public static Task<DebugAdapterServer> StartAsync(PipeReader input, PipeWriter output, LuaDebugAdapter adapter,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        return DebugAdapterServer.From(options => Configure(options, adapter, input, output, loggerFactory),
            cancellationToken);
    }

    private static void Configure(DebugAdapterServerOptions options, LuaDebugAdapter adapter, PipeReader input,
        PipeWriter output, ILoggerFactory loggerFactory)
    {
        options
            .WithInput(input)
            .WithOutput(output)
            .WithLoggerFactory(loggerFactory)
            .OnInitialize((server, _, _) =>
            {
                adapter.Bind(server);
                return Task.CompletedTask;
            })
            .OnAttach(Surfacing<LuaAttachArguments, AttachResponse>(adapter.AttachAsync))
            .OnLaunch(Surfacing<LuaLaunchArguments, LaunchResponse>(adapter.LaunchAsync))
            .OnConfigurationDone(
                Surfacing<ConfigurationDoneArguments, ConfigurationDoneResponse>(adapter.ConfigurationDoneAsync))
            .OnDisconnect(Surfacing<DisconnectArguments, DisconnectResponse>(adapter.DisconnectAsync))
            .OnTerminate(Surfacing<TerminateArguments, TerminateResponse>(adapter.TerminateAsync))
            .OnThreads(Surfacing<ThreadsArguments, ThreadsResponse>(adapter.ThreadsAsync))
            .OnStackTrace(Surfacing<StackTraceArguments, StackTraceResponse>(adapter.StackTraceAsync))
            .OnScopes(Surfacing<ScopesArguments, ScopesResponse>(adapter.ScopesAsync))
            .OnVariables(Surfacing<VariablesArguments, VariablesResponse>(adapter.VariablesAsync))
            .OnEvaluate(Surfacing<EvaluateArguments, EvaluateResponse>(adapter.EvaluateAsync))
            .OnSetBreakpoints(Surfacing<SetBreakpointsArguments, SetBreakpointsResponse>(adapter.SetBreakpointsAsync))
            .OnContinue(Surfacing<ContinueArguments, ContinueResponse>(adapter.ContinueAsync))
            .OnNext(Surfacing<NextArguments, NextResponse>(adapter.NextAsync))
            .OnStepIn(Surfacing<StepInArguments, StepInResponse>(adapter.StepInAsync))
            .OnStepOut(Surfacing<StepOutArguments, StepOutResponse>(adapter.StepOutAsync))
            .OnPause(Surfacing<PauseArguments, PauseResponse>(adapter.PauseAsync))
            .OnLoadedSources(Surfacing<LoadedSourcesArguments, LoadedSourcesResponse>(adapter.LoadedSourcesAsync))
            // The scripts view's own requests; VS Code sends them through DebugSession.customRequest.
            .OnRequest(LuaCustomMessages.Scripts, Surfacing<LuaScriptsResult>(adapter.ScriptsAsync))
            .OnRequest(LuaCustomMessages.Refresh,
                Surfacing<LuaRefreshArguments?, LuaScriptsResult>(adapter.RefreshAsync))
            .OnRequest(LuaCustomMessages.SelectScript,
                Surfacing<LuaSelectScriptArguments, LuaScriptsResult>(adapter.SelectScriptAsync))
            .OnRequest(LuaCustomMessages.BreakThread,
                Surfacing<LuaBreakThreadArguments, LuaScriptsResult>(adapter.BreakThreadAsync));

        CopyCapabilities(LuaDebugAdapter.Capabilities, options.Capabilities);
    }

    /// <summary>
    ///     Every refusal the session or the adapter raises is meant for the user, so it becomes a
    ///     request error that keeps its message; the protocol library reports anything else as a
    ///     bare internal error.
    /// </summary>
    private static Func<TArgs, CancellationToken, Task<TResult>> Surfacing<TArgs, TResult>(
        Func<TArgs, CancellationToken, Task<TResult>> handler)
    {
        return async (args, cancellationToken) =>
        {
            try
            {
                return await handler(args, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not RpcErrorException and not OperationCanceledException)
            {
                throw new LuaDebugAdapterException(e.Message, e);
            }
        };
    }

    private static Func<CancellationToken, Task<TResult>> Surfacing<TResult>(
        Func<CancellationToken, Task<TResult>> handler)
    {
        return async cancellationToken =>
        {
            try
            {
                return await handler(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not RpcErrorException and not OperationCanceledException)
            {
                throw new LuaDebugAdapterException(e.Message, e);
            }
        };
    }

    private static void CopyCapabilities(InitializeResponse from,
        OmniSharp.Extensions.DebugAdapter.Protocol.Models.Capabilities to)
    {
        to.SupportsConfigurationDoneRequest = from.SupportsConfigurationDoneRequest;
        to.SupportsEvaluateForHovers = from.SupportsEvaluateForHovers;
        to.SupportsLoadedSourcesRequest = from.SupportsLoadedSourcesRequest;
        to.SupportsTerminateRequest = from.SupportsTerminateRequest;
        to.SupportTerminateDebuggee = from.SupportTerminateDebuggee;
        to.SupportsConditionalBreakpoints = from.SupportsConditionalBreakpoints;
        to.SupportsHitConditionalBreakpoints = from.SupportsHitConditionalBreakpoints;
        to.SupportsLogPoints = from.SupportsLogPoints;
        to.SupportsSetVariable = from.SupportsSetVariable;
        to.SupportsRestartRequest = from.SupportsRestartRequest;
        to.SupportsDelayedStackTraceLoading = from.SupportsDelayedStackTraceLoading;
        to.ExceptionBreakpointFilters = from.ExceptionBreakpointFilters;
    }
}