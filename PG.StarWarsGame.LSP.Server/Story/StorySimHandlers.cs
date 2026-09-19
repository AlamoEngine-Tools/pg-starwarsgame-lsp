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
        var (state, error) = sim.Start(new StoryModelKey(request.Campaign, request.Faction));
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
        var (state, error) = sim.Stop(new StoryModelKey(request.Campaign, request.Faction));
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
        var (state, error) = sim.GetState(new StoryModelKey(request.Campaign, request.Faction), request.SinceSeq);
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
        var (state, error) = sim.SatisfyTrigger(new StoryModelKey(request.Campaign, request.Faction), request.NodeId,
            request.SinceSeq);
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
        var (state, error) =
            sim.SetFlag(new StoryModelKey(request.Campaign, request.Faction), request.Flag, request.Value,
                request.SinceSeq);
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
        var (state, error) = sim.AdvanceClock(new StoryModelKey(request.Campaign, request.Faction), request.Seconds,
            request.SinceSeq);
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
        var (state, error) = sim.LuaNotify(new StoryModelKey(request.Campaign, request.Faction), request.Id,
            request.SinceSeq);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimTickHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimTickParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimTickParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.Tick(new StoryModelKey(request.Campaign, request.Faction), request.Count,
            request.SinceSeq);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimRunToDecisionHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimRunToDecisionParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimRunToDecisionParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.RunToDecision(new StoryModelKey(request.Campaign, request.Faction), request.SinceSeq);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimSeekHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimSeekParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimSeekParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.Seek(new StoryModelKey(request.Campaign, request.Faction), request.Tick);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimWorldHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimWorldParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimWorldParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.ApplyWorldChange(new StoryModelKey(request.Campaign, request.Faction),
            request.Change, request.SinceSeq);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}

public sealed class StorySimBreakpointsHandler(IStorySimulationService sim, ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<StorySimBreakpointsParams, StorySimStateResult>
{
    public Task<StorySimStateResult> Handle(StorySimBreakpointsParams request, CancellationToken ct)
    {
        if (StorySimFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new StorySimStateResult(null, rejection));
        var (state, error) = sim.SetBreakpoints(new StoryModelKey(request.Campaign, request.Faction),
            request.NodeIds, request.OnConditionalGates);
        return Task.FromResult(new StorySimStateResult(state, error));
    }
}