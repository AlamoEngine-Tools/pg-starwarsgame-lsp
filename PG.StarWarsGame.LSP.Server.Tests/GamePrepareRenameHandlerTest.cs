// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Features;
using PG.StarWarsGame.LSP.Core.Util;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     Prepare-rename routes as rename does (#312): to the first provider that handles the document,
///     by no file extension of the handler's own.
/// </summary>
public sealed class GamePrepareRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string LuaUri = "file:///script.lua";
    private const string TxtUri = "file:///data.txt";

    private static PrepareRenameParams PrepareAt(string uri, int line = 0, int character = 0)
    {
        return new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character)
        };
    }

    private static GamePrepareRenameHandler Build(params IRenameProvider[] providers)
    {
        return new GamePrepareRenameHandler(providers, new FileHelper(new MockFileSystem()));
    }

    [Fact]
    public async Task Handle_NoProviderHandlesTheDocument_AnswersNothing()
    {
        var xml = new FakeProvider(".xml", new RangeOrPlaceholderRange(new Range()));

        var result = await Build(xml).Handle(PrepareAt(TxtUri), CancellationToken.None);

        Assert.Null(result);
        Assert.False(xml.WasCalled);
    }

    [Fact]
    public async Task Handle_AnXmlFile_ReachesTheXmlProvider()
    {
        var range = new RangeOrPlaceholderRange(new Range());
        var lua = new FakeProvider(".lua", new RangeOrPlaceholderRange(new Range()));

        var result = await Build(new FakeProvider(".xml", range), lua)
            .Handle(PrepareAt(XmlUri), CancellationToken.None);

        Assert.Same(range, result);
        Assert.False(lua.WasCalled);
    }

    [Fact]
    public async Task Handle_ALuaFile_ReachesTheLuaProvider()
    {
        var range = new RangeOrPlaceholderRange(new Range());
        var xml = new FakeProvider(".xml", new RangeOrPlaceholderRange(new Range()));

        var result = await Build(xml, new FakeProvider(".lua", range))
            .Handle(PrepareAt(LuaUri), CancellationToken.None);

        Assert.Same(range, result);
        Assert.False(xml.WasCalled);
    }

    [Fact]
    public async Task Handle_ATextFile_ReachesTheProviderThatHandlesIt()
    {
        var range = new RangeOrPlaceholderRange(new Range());

        var result = await Build(new FakeProvider(".xml", null), new FakeProvider(".txt", range))
            .Handle(PrepareAt(TxtUri), CancellationToken.None);

        Assert.Same(range, result);
    }

    [Fact]
    public async Task Handle_TheProviderRefuses_AnswersNothing()
    {
        var result = await Build(new FakeProvider(".xml", null)).Handle(PrepareAt(XmlUri), CancellationToken.None);

        Assert.Null(result);
    }

    private sealed class FakeProvider(string extension, RangeOrPlaceholderRange? range) : IRenameProvider
    {
        public bool WasCalled { get; private set; }

        public bool Handles(string documentUri)
        {
            return documentUri.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        public Task<WorkspaceEdit?> RenameAsync(RenameParams request, CancellationToken ct)
        {
            throw new InvalidOperationException("prepare never renames");
        }

        public Task<RangeOrPlaceholderRange?> PrepareAsync(PrepareRenameParams request, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(range);
        }
    }
}