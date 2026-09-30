// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using PG.StarWarsGame.LSP.Server.Story;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke test for the story simulator (#90) against the vanilla foc/ workspace: start the
///     Underworld campaign, run to its first decision, answer it, watch the event fire, rewind to
///     tick 0, stop. One scenario through the real LSP wire, so the request shapes, the session
///     bookkeeping and the trace delta contract are all exercised together.
/// </summary>
[Trait("Category", "E2E")]
public sealed class StorySimulatorSmokeTest : IClassFixture<LspServerFixture>
{
    // The FoC registry ships one story campaign; EaW comments its main campaigns out.
    private const string Campaign = "Full_Story_Campaign_Underworld";
    private const string Faction = "Underworld";

    private readonly LspServerFixture _fixture;

    public StorySimulatorSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Underworld_RunsToTheFirstDecision_AnswersIt_RewindsAndStopsAsync()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var started = await SendAsync(new StorySimStartParams(Campaign, Faction));
        try
        {
            Assert.True(started.Running);
            Assert.Equal(0, started.Tick);
            Assert.NotEmpty(started.Nodes);
            // The seed: Underworld holds planets at Start, because the campaign XML places forces.
            Assert.NotEmpty(started.World.Planets);
            Assert.Contains(started.World.Planets, p => p.Owner is not null);
            // The load steps arm the roots; every step so far is tick 0.
            Assert.All(started.Steps, s => Assert.Equal(0, s.Tick));
            Assert.Equal(started.Steps.Count, started.TotalSteps);

            var atDecision = await SendAsync(
                new StorySimRunToDecisionParams(Campaign, Faction, started.TotalSteps));
            Assert.True(atDecision.Running);
            Assert.True(atDecision.Interventions.Count > 0 || atDecision.HaltedAt is not null,
                "Run to decision returned with nothing waiting on the author and no breakpoint hit.");
            // Only the steps after the ones already seen come back.
            Assert.All(atDecision.Steps, s => Assert.True(s.Seq >= started.TotalSteps));

            var decision = atDecision.Interventions[0];

            // A battle waiting on the author is answered by resolving it, not by satisfying a
            // trigger: its node is the tactical entry, and while it holds the galaxy still nothing
            // else can fire anyway. Underworld opens on one, so this is the path it takes.
            if (decision.BattleKey is { } battleKey)
            {
                var resolved = await SendAsync(
                    new StorySimResolveBattleParams(Campaign, Faction, battleKey, true, atDecision.TotalSteps));

                Assert.True(resolved.Running);
                Assert.True(resolved.TotalSteps > atDecision.TotalSteps);
                // The galaxy took the outcome, so it is no longer the decision on the table.
                Assert.DoesNotContain(resolved.Interventions,
                    i => i.BattleKey == battleKey && i.NodeId == decision.NodeId);
            }
            else
            {
                var answered = decision.Suggested is not null
                    ? await SendAsync(new StorySimWorldParams(Campaign, Faction, decision.Suggested,
                        atDecision.TotalSteps))
                    : await SendAsync(new StorySimSatisfyTriggerParams(Campaign, Faction, decision.NodeId,
                        atDecision.TotalSteps));

                var fired = answered.Nodes.Single(n => n.NodeId == decision.NodeId);
                Assert.Equal("Fired", fired.Lifecycle);
                Assert.True(fired.FireCount >= 1);
                Assert.Contains(answered.Steps, s => s.NodeId == decision.NodeId && s.To == "Fired");
                Assert.True(answered.TotalSteps > atDecision.TotalSteps);
            }

            var rewound = await SendAsync(new StorySimSeekParams(Campaign, Faction, 0));
            Assert.Equal(0, rewound.Tick);
            // A rewind hands the trace back from the top.
            Assert.Equal(rewound.Steps.Count, rewound.TotalSteps);
            Assert.All(rewound.Steps, s => Assert.Equal(0, s.Tick));

            // A battle's decision is its tactical entry rather than a story node, so only an
            // event decision has a lifecycle to be back to.
            if (decision.BattleKey is null)
                Assert.Equal("Armed", rewound.Nodes.Single(n => n.NodeId == decision.NodeId).Lifecycle);
        }
        finally
        {
            var stopped = await SendAsync(new StorySimStopParams(Campaign, Faction));
            Assert.False(stopped.Running);
        }
    }

    private async Task<StorySimStateDto> SendAsync<TParams>(TParams request)
        where TParams : IRequest<StorySimStateResult>
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await _fixture.Client.SendRequest(request, cts.Token);
        Assert.True(result.Error is null, result.Error);
        Assert.NotNull(result.State);
        return result.State!;
    }

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");
    }
}