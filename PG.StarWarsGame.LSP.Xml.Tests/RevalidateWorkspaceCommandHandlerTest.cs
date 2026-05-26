// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Xml.Commands;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class RevalidateWorkspaceCommandHandlerTest
{
    [Fact]
    public async Task Handle_CallsPublisherRevalidateWorkspace()
    {
        var fake = new FakeRevalidator();
        var handler = new RevalidateWorkspaceCommandHandler(fake);

        await handler.ExecuteAsync(CancellationToken.None);

        Assert.True(fake.WorkspaceCalled);
    }

    private sealed class FakeRevalidator : IXmlDiagnosticsRevalidator
    {
        public bool WorkspaceCalled { get; private set; }

        public Task RevalidateWorkspaceAsync(CancellationToken ct)
        {
            WorkspaceCalled = true;
            return Task.CompletedTask;
        }

        public Task RevalidateDocumentAsync(string uri, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}