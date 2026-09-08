// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Symbols;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     <c>aet/resolveReference</c> against the real server and the real workspace.
/// </summary>
/// <remarks>
///     <para>
///         This is the jump behind an ability row. The row carries a NAME and the type it should be
///         and nothing else - the server assembled it out of several files, so there is no document
///         position for <c>textDocument/definition</c> to work from.
///     </para>
///     <para>
///         The endpoint is deliberately ungated. The same lookup already existed as
///         <c>aet/resolveStoryReference</c>, reachable only while the story editor was switched on,
///         which is no basis for the model preview to be able to open an ability.
///     </para>
/// </remarks>
[Trait("Category", "E2E")]
public sealed class ResolveReferenceSmokeTest(LspServerFixture fixture) : IClassFixture<LspServerFixture>
{
    /// <summary>
    ///     An owner-scoped ability, which is the interesting shape.
    /// </summary>
    /// <remarks>
    ///     It is indexed as <c>OWNER$Red_Squadron_Lucky_Shot</c> while every reference to it - the
    ///     <c>GUI_Activated_Ability_Name</c> that names it - carries the bare name. The scoped-name
    ///     fallback is what closes that gap, and nothing but a real workspace exercises it.
    /// </remarks>
    private const string AbilityName = "Red_Squadron_Lucky_Shot";

    // The tag is declared twice in the schema with DIFFERENT reference types - SpecialAbility under
    // GameObjectType, UnitAbility under UnitAbility - and a panel has no way to know which parent
    // its row came from. Both have to land on the definition, which they do because a type that
    // matches nothing falls back to the untyped winner rather than failing.
    [Theory]
    [InlineData("SpecialAbility")]
    [InlineData("UnitAbility")]
    [InlineData(null)]
    public async Task ScopedAbilityName_ResolvesToItsDefiningLine(string? referenceType)
    {
        RequireWorkspace();
        await WaitForScanAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await fixture.Client.SendRequest(
            new ResolveReferenceParams(AbilityName, referenceType), cts.Token);

        Assert.True(result.Error is null, result.Error);
        Assert.NotNull(result.Uri);

        // Asserting the LINE, not just the file: a lookup that found the right document and pointed
        // at its first line would look like a working go-to and send the reader nowhere useful.
        var lines = await File.ReadAllLinesAsync(new Uri(result.Uri!).LocalPath, cts.Token);
        Assert.InRange(result.Line, 0, lines.Length - 1);
        Assert.Contains(AbilityName, lines[result.Line], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownName_ExplainsItselfRatherThanReturningAnEmptyLocation()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await fixture.Client.SendRequest(
            new ResolveReferenceParams("Definitely_Not_An_Ability", "SpecialAbility"), cts.Token);

        Assert.Null(result.Uri);
        Assert.Contains("Definitely_Not_An_Ability", result.Error);
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
