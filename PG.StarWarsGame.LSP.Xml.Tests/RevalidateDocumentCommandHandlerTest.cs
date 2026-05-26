// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Xml.Commands;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class RevalidateDocumentCommandHandlerTest
{
    [Fact]
    public async Task Handle_ExtractsUriAndCallsPublisherRevalidateDocument()
    {
        const string uri = "file:///test.xml";
        var fake = new FakeRevalidator();
        var handler = new RevalidateDocumentCommandHandler(fake);

        await handler.ExecuteAsync(uri, CancellationToken.None);

        Assert.Equal(uri, fake.LastUri);
    }

    private sealed class FakeRevalidator : IXmlDiagnosticsRevalidator
    {
        public string? LastUri { get; private set; }

        public Task RevalidateWorkspaceAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task RevalidateDocumentAsync(string uri, CancellationToken ct)
        {
            LastUri = uri;
            return Task.CompletedTask;
        }
    }
}