// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Parsing;

/// <summary>
///     Every parsed Lua script is indexed as a navigable workspace-file symbol keyed by its
///     extensionless name, so a manifest <c>&lt;Lua_Script&gt;</c> (a workspaceFile reference)
///     resolves to it for go-to / rename.
/// </summary>
public sealed class LuaWorkspaceFileSymbolTest
{
    [Fact]
    public async Task Script_EmitsWorkspaceFileSymbol_KeyedByExtensionlessName()
    {
        var parser = new LuaGameDocumentParser(
            new LuaApiSchemaProvider([]),
            new FileHelper(new MockFileSystem()),
            NullLogger<LuaGameDocumentParser>.Instance,
            new LuaAnnotationRepository());

        var index = await parser.ParseAsync(
            "file:///ws/data/scripts/story/Story_Rebel_Act_III.lua",
            "function Foo() end", 1, CancellationToken.None);

        var symbol = Assert.Single(index.Symbols, s => s.Kind == GameSymbolKind.WorkspaceFile);
        Assert.Equal("luascript:story_rebel_act_iii", symbol.Id);
        Assert.Equal("LuaScript", symbol.TypeName);
        var origin = Assert.IsType<FileOrigin>(symbol.Origin);
        Assert.Equal(0, origin.Line);
        Assert.Equal(0, origin.Column);
    }
}
