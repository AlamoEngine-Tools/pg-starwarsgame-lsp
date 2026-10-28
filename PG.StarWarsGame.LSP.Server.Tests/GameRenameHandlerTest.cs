// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Features;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     The rename handler knows no file extension (#312): it hands the request to the first provider
///     that says it handles the document, and answers nothing when none does. What a language does
///     with the request - the Lua provider hands locals to the analyzer - is the provider's own.
/// </summary>
public sealed class GameRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string LuaUri = "file:///script.lua";
    private const string TxtUri = "file:///data.txt";

    private static RenameParams RenameAt(string uri, int line = 0, int character = 0, string newName = "NEW")
    {
        return new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character),
            NewName = newName
        };
    }

    private static GameRenameHandler Build(params IRenameProvider[] providers)
    {
        return new GameRenameHandler(providers, new FileHelper(new MockFileSystem()));
    }

    [Fact]
    public async Task Handle_NoProviderHandlesTheDocument_AnswersNothing()
    {
        var xml = new FakeProvider(".xml", new WorkspaceEdit());
        var lua = new FakeProvider(".lua", new WorkspaceEdit());

        var result = await Build(xml, lua).Handle(RenameAt(TxtUri), CancellationToken.None);

        Assert.Null(result);
        Assert.False(xml.WasCalled);
        Assert.False(lua.WasCalled);
    }

    [Fact]
    public async Task Handle_AnXmlFile_ReachesTheXmlProvider()
    {
        var xmlEdit = new WorkspaceEdit();
        var lua = new FakeProvider(".lua", new WorkspaceEdit());

        var result = await Build(new FakeProvider(".xml", xmlEdit), lua)
            .Handle(RenameAt(XmlUri), CancellationToken.None);

        Assert.Same(xmlEdit, result);
        Assert.False(lua.WasCalled);
    }

    [Fact]
    public async Task Handle_ALuaFile_ReachesTheLuaProvider()
    {
        var luaEdit = new WorkspaceEdit();
        var xml = new FakeProvider(".xml", new WorkspaceEdit());

        var result = await Build(xml, new FakeProvider(".lua", luaEdit))
            .Handle(RenameAt(LuaUri), CancellationToken.None);

        Assert.Same(luaEdit, result);
        Assert.False(xml.WasCalled);
    }

    /// <summary>A text file reaches rename the moment a provider handles it; the handler needs no edit.</summary>
    [Fact]
    public async Task Handle_ATextFile_ReachesTheProviderThatHandlesIt()
    {
        var txtEdit = new WorkspaceEdit();

        var result = await Build(new FakeProvider(".xml", null), new FakeProvider(".txt", txtEdit))
            .Handle(RenameAt(TxtUri), CancellationToken.None);

        Assert.Same(txtEdit, result);
    }

    [Fact]
    public async Task Handle_TheProviderRefuses_AnswersNothing()
    {
        var result = await Build(new FakeProvider(".xml", null)).Handle(RenameAt(XmlUri), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_TwoProvidersHandleTheDocument_TheFirstRegisteredAnswers()
    {
        var first = new FakeProvider(".xml", new WorkspaceEdit());
        var second = new FakeProvider(".xml", new WorkspaceEdit());

        var result = await Build(first, second).Handle(RenameAt(XmlUri), CancellationToken.None);

        Assert.Same(first.Edit, result);
        Assert.False(second.WasCalled);
    }

    private sealed class FakeProvider(string extension, WorkspaceEdit? edit) : IRenameProvider
    {
        public bool WasCalled { get; private set; }
        public WorkspaceEdit? Edit => edit;

        public bool Handles(string documentUri)
        {
            return documentUri.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        public Task<WorkspaceEdit?> RenameAsync(RenameParams request, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(edit);
        }

        public Task<RangeOrPlaceholderRange?> PrepareAsync(PrepareRenameParams request, CancellationToken ct)
        {
            throw new InvalidOperationException("rename never prepares");
        }
    }
}