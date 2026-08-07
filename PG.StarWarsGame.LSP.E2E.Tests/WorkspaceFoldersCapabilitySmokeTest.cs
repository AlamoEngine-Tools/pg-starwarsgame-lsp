// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Multi-root support is only reachable if the server tells the client it wants workspace-folder
///     change notifications - VS Code sends <c>workspace/didChangeWorkspaceFolders</c> only to a
///     server that advertised <c>workspace.workspaceFolders.supported</c>. That advertisement is a
///     side effect of registering <c>GameDidChangeWorkspaceFoldersHandler</c>, so nothing in the
///     unit suite would notice it disappearing.
/// </summary>
[Trait("Category", "E2E")]
public sealed class WorkspaceFoldersCapabilitySmokeTest : IClassFixture<EawLspServerFixture>
{
    private readonly EawLspServerFixture _fixture;

    public WorkspaceFoldersCapabilitySmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void ServerCapabilities_AdvertiseWorkspaceFolderChangeNotifications()
    {
        var workspace = _fixture.Client.ServerSettings.Capabilities?.Workspace;

        Assert.NotNull(workspace);
        Assert.True(workspace!.WorkspaceFolders?.Supported,
            "workspace.workspaceFolders.supported must be true or the client never reports folder changes");
        // Asserted on the wire form: changeNotifications is a bool-or-string union whose .NET shape
        // is an implementation detail, but what reaches the client has to be a truthy value.
        var changeNotifications = JsonConvert.SerializeObject(workspace.WorkspaceFolders!.ChangeNotifications);
        Assert.False(changeNotifications is "null" or "false" or "\"\"",
            $"workspace.workspaceFolders.changeNotifications must be set for the client to subscribe, was {changeNotifications}");
    }
}
