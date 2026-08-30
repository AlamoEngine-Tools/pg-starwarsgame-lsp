// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The Gameplay lens' ability rows, against the real server and the real workspace.
/// </summary>
/// <remarks>
///     <para>
///         The unit tests pin the precedence with fakes; this proves the whole path actually joins
///         up - the scene handler resolving the workspace's icon catalog, the builder reading the
///         <c>Alternate_*</c> trio, the text convention hitting the shipped master text file, and the
///         icon coming back as pixels.
///     </para>
///     <para>
///         <c>Nebulon_B_Frigate</c> declares a <c>DEFEND</c> ability, which is a good subject: it is
///         stat-only, so before this its row was a switch with nothing behind it, and its
///         <c>TEXT_TOOLTIP_ABILITY_DEFEND_NAME</c> / <c>_DESCRIPTION</c> pair is in the shipped text.
///     </para>
/// </remarks>
[Trait("Category", "E2E")]
public sealed class PreviewAbilityTextSmokeTest(LspServerFixture fixture) : IClassFixture<LspServerFixture>
{
    private const string ObjectId = "Nebulon_B_Frigate";

    [Fact]
    public async Task AbilityRow_CarriesItsLocalisedNameAndDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await fixture.Client.SendRequest(
            new GetPreviewSceneParams { ObjectId = ObjectId }, cts.Token);

        var abilities = result.Scene.Abilities;
        Assert.NotEmpty(abilities);

        var defend = abilities.FirstOrDefault(a =>
            string.Equals(a.Type, "DEFEND", StringComparison.OrdinalIgnoreCase));
        Assert.True(defend is not null,
            $"{ObjectId} declares no DEFEND ability; it has: "
            + string.Join(", ", abilities.Select(a => a.Type)));

        // Straight from TEXT_TOOLTIP_ABILITY_DEFEND_NAME / _DESCRIPTION. Asserting the values rather
        // than mere non-emptiness: a convention that silently resolved to the wrong key would still
        // return SOMETHING, and that is the failure worth catching.
        Assert.Equal("Boost Shield Power", defend.Name);
        Assert.NotNull(defend.Description);
        Assert.Contains("shield", defend.Description, StringComparison.OrdinalIgnoreCase);

        // The icon comes the other way entirely - I_SA_DEFEND out of the workspace's mega texture,
        // decoded to PNG - so it is the half of the row the text assertions say nothing about.
        Assert.NotNull(defend.IconDataUri);
        Assert.StartsWith("data:image/png;base64,", defend.IconDataUri);
    }

    /// <summary>
    ///     An ability that names a definition, which is what the row's jump acts on.
    /// </summary>
    /// <remarks>
    ///     <c>Red_Squadron_Container</c>'s <c>LUCKY_SHOT</c> carries
    ///     <c>GUI_Activated_Ability_Name</c>, and the shipped file writes it with whitespace around
    ///     the value. The row has to hand the client something resolvable;
    ///     <c>aet/resolveReference</c> - covered by its own smoke test - turns it into a position.
    /// </remarks>
    [Fact]
    public async Task AbilityRow_CarriesTheDefinitionItNames()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await fixture.Client.SendRequest(
            new GetPreviewSceneParams { ObjectId = "Red_Squadron_Container" }, cts.Token);

        var lucky = result.Scene.Abilities.FirstOrDefault(a =>
            string.Equals(a.Type, "LUCKY_SHOT", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(lucky);

        Assert.Equal("Red_Squadron_Lucky_Shot", lucky.GuiName?.Trim());
        Assert.Equal("Lucky Shot", lucky.Name);
    }

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");
    }
}
