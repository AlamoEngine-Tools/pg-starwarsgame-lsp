// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Server.Commands;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Commands;

public sealed class ReloadProjectCommandHandlerTest
{
    [Fact]
    public async Task Handle_DelegatesToReloadAsync()
    {
        var reloadService = new StubReloadService();
        var handler = new ReloadProjectCommandHandler(reloadService);

        await handler.Handle(new ExecuteCommandParams(), CancellationToken.None);

        Assert.Equal(1, reloadService.ReloadCallCount);
    }

    private sealed class StubReloadService : IModProjectReloadService
    {
        public int ReloadCallCount { get; private set; }

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            ReloadCallCount++;
            return Task.CompletedTask;
        }
    }
}
