// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Story;

public sealed class StorySimStartHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimStartParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimStartParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.Start(request.Campaign);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimStopHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimStopParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimStopParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.Stop(request.Campaign);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimGetStateHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimGetStateParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimGetStateParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.GetState(request.Campaign);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimSatisfyTriggerHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimSatisfyTriggerParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimSatisfyTriggerParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.SatisfyTrigger(request.Campaign, request.NodeId);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimSetFlagHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimSetFlagParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimSetFlagParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.SetFlag(request.Campaign, request.Flag, request.Value);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimAdvanceClockHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimAdvanceClockParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimAdvanceClockParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.AdvanceClock(request.Campaign, request.Seconds);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimLuaNotifyHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimLuaNotifyParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimLuaNotifyParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.LuaNotify(request.Campaign, request.Id);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}