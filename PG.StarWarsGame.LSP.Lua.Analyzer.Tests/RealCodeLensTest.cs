// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>A function's usage lens counts its uses, as the server resolves it.</summary>
/// <remarks>Local only, see <see cref="VanillaScriptStack" />.</remarks>
public sealed class RealCodeLensTest
{
    private const string Script = "Gameobject/Interdictor.lua";

    // Under_Missile_Attack is defined on line 111 and called on lines 94 and 125. The analyzer
    // counts the definition as a third (measured, 0.25.1).
    [Fact]
    public async Task AFunctionsLens_CountsItsCalls_NotItsDefinition()
    {
        await using var stack = VanillaScriptStack.Create(Script);
        var script = stack.UriOf(Script);
        var reported = false;
        stack.Analyzer.DiagnosticsPublished += p => reported |= DocumentUris.Same(p.Uri.ToString(), script);
        stack.Analyzer.DidOpen(script, await File.ReadAllTextAsync(stack.FileHelper.FileUriToPath(script)!,
            TestContext.Current.CancellationToken), 1);
        await VanillaScriptStack.UntilAsync(() => reported, TimeSpan.FromSeconds(30));
        var handler = new LuaCodeLensHandler(stack.Analyzer, new NoScripts(stack.FileHelper), new LuaStubLocation());

        var lenses = await handler.Handle(
            new CodeLensParams { TextDocument = new TextDocumentIdentifier(DocumentUri.From(script)) },
            TestContext.Current.CancellationToken);
        var lens = Assert.Single(lenses ?? [], l => l.Range.Start.Line == 110);
        var resolved = await handler.Handle(lens, TestContext.Current.CancellationToken);

        Assert.Equal("2 usages", resolved.Command?.Title);
    }

    private sealed class NoScripts(IFileHelper fileHelper) : ILuaSandboxProvider
    {
        public LuaSandbox Current { get; } =
            LuaSandbox.Build(ImmutableDictionary<string, DocumentIndex>.Empty, fileHelper);
    }
}