// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>A vanilla script's outline (Ctrl+Shift+O) lists its functions, as the server answers it.</summary>
/// <remarks>Local only, see <see cref="VanillaScriptStack" />.</remarks>
public sealed class RealDocumentSymbolTest
{
    [Fact]
    public async Task AScriptsOutline_ListsItsFunctions()
    {
        await using var stack = VanillaScriptStack.Create("Gameobject/Interdictor.lua");
        var script = stack.UriOf("Gameobject/Interdictor.lua");
        var reported = false;
        stack.Analyzer.DiagnosticsPublished += p => reported |= DocumentUris.Same(p.Uri.ToString(), script);
        stack.Analyzer.DidOpen(script, await File.ReadAllTextAsync(stack.FileHelper.FileUriToPath(script)!,
            TestContext.Current.CancellationToken), 1);
        await VanillaScriptStack.UntilAsync(() => reported, TimeSpan.FromSeconds(30));

        var symbols = await new LuaDocumentSymbolHandler(stack.Analyzer).Handle(new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(script))
        }, TestContext.Current.CancellationToken);

        var names = (symbols ?? []).Select(s => s.DocumentSymbol?.Name ?? s.SymbolInformation?.Name).ToList();
        Assert.Subset(new HashSet<string>
        {
            "Definitions", "State_Init", "Cancel_Interdiction", "Under_Missile_Attack", "Cancel_Missile_Shield"
        }, names.OfType<string>().ToHashSet());
    }

    // The editor asks for the outline the moment it opens a script and keeps the answer until the
    // script changes: one asked before the script's analyzer is up is the outline for good.
    [Fact]
    public async Task AScriptsOutline_AskedAsItOpens_ListsItsFunctions()
    {
        await using var stack = VanillaScriptStack.Create("Gameobject/Interdictor.lua");
        var script = stack.UriOf("Gameobject/Interdictor.lua");
        stack.Analyzer.DidOpen(script, await File.ReadAllTextAsync(stack.FileHelper.FileUriToPath(script)!,
            TestContext.Current.CancellationToken), 1);

        var symbols = await new LuaDocumentSymbolHandler(stack.Analyzer).Handle(new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(script))
        }, TestContext.Current.CancellationToken);

        var names = (symbols ?? []).Select(s => s.DocumentSymbol?.Name ?? s.SymbolInformation?.Name).OfType<string>();
        Assert.Contains("Definitions", names);
    }
}