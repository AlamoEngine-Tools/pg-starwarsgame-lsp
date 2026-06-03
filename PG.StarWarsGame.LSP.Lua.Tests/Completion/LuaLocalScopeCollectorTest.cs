// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Completion;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Completion;

public sealed class LuaLocalScopeCollectorTest
{
    private const string DocUri = "file:///data/scripts/myscript.lua";
    private const string LibUri = "file:///data/scripts/library/pgstatemachine.lua";

    private static readonly IFileHelper FileHelper = new FileHelper(new MockFileSystem());
    private static readonly ILuaApiSchemaProvider Schema = new LuaApiSchemaProvider([]);

    private static IReadOnlyList<ScopeEntry> Collect(
        string text, int line = 0, int character = 0,
        GameIndex? index = null, string? docUri = null)
    {
        return LuaLocalScopeCollector.CollectAt(
            text, line, character,
            docUri ?? DocUri,
            index ?? GameIndex.Empty,
            Schema,
            FileHelper);
    }

    // ── local variables ───────────────────────────────────────────────────────

    [Fact]
    public void LocalDeclaredBeforeCursor_IsInScope()
    {
        // line 0: local x = 1
        // line 1: cursor here (line 1, col 0)
        const string text = "local x = 1\n";
        var entries = Collect(text, 1);
        Assert.Contains(entries, e => e.Name == "x" && e.Kind == ScopeEntryKind.LocalVariable);
    }

    [Fact]
    public void LocalDeclaredAfterCursor_IsNotInScope()
    {
        // cursor on line 0, local on line 1
        const string text = "\nlocal x = 1";
        var entries = Collect(text);
        Assert.DoesNotContain(entries, e => e.Name == "x");
    }

    [Fact]
    public void MultipleLocals_OnlyBeforeCursorArePresent()
    {
        // local a on line 0, local b on line 2 — cursor on line 1
        const string text = "local a = 1\n\nlocal b = 2";
        var entries = Collect(text, 1);
        Assert.Contains(entries, e => e.Name == "a");
        Assert.DoesNotContain(entries, e => e.Name == "b");
    }

    [Fact]
    public void LocalInsideFunction_NotVisibleOutside()
    {
        // local y declared inside Foo, cursor is at file scope after Foo
        const string text = "function Foo()\n    local y = 1\nend\n";
        var entries = Collect(text, 3);
        Assert.DoesNotContain(entries, e => e.Name == "y");
    }

    // ── parameters ───────────────────────────────────────────────────────────

    [Fact]
    public void Parameter_InsideFunction_IsInScope()
    {
        // function Foo(param) — cursor inside the function body
        const string text = "function Foo(param)\n    \nend";
        var entries = Collect(text, 1, 4);
        Assert.Contains(entries, e => e.Name == "param" && e.Kind == ScopeEntryKind.Parameter);
    }

    [Fact]
    public void Parameter_OutsideFunction_IsNotInScope()
    {
        // cursor at line 3 (after end)
        const string text = "function Foo(param)\nend\n";
        var entries = Collect(text, 2);
        Assert.DoesNotContain(entries, e => e.Name == "param");
    }

    [Fact]
    public void MultipleParameters_AllInScope()
    {
        const string text = "function Foo(a, b, c)\n    \nend";
        var entries = Collect(text, 1, 4);
        Assert.Contains(entries, e => e.Name == "a");
        Assert.Contains(entries, e => e.Name == "b");
        Assert.Contains(entries, e => e.Name == "c");
    }

    // ── own globals ───────────────────────────────────────────────────────────

    [Fact]
    public void OwnGlobal_DefinedInCurrentFile_IsInScope()
    {
        var sym = new GameSymbol("MyGlobal", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(DocUri, 0, null), null);
        var docIndex = new DocumentIndex(DocUri, 1, [sym], []);
        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(DocUri, docIndex),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("MyGlobal", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var entries = Collect("", 0, 0, index);
        Assert.Contains(entries, e => e.Name == "MyGlobal" && e.Kind == ScopeEntryKind.OwnGlobal);
    }

    [Fact]
    public void OtherFileGlobal_NotRequired_NotInScope()
    {
        const string otherUri = "file:///data/scripts/other.lua";
        var sym = new GameSymbol("OtherGlobal", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(otherUri, 0, null), null);
        var otherDoc = new DocumentIndex(otherUri, 1, [sym], []);
        var myDoc = new DocumentIndex(DocUri, 1, [], []);
        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(DocUri, myDoc)
                .Add(otherUri, otherDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("OtherGlobal", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var entries = Collect("", 0, 0, index);
        Assert.DoesNotContain(entries, e => e.Name == "OtherGlobal");
    }

    // ── required globals ─────────────────────────────────────────────────────

    [Fact]
    public void RequiredLibraryGlobal_IsInScope()
    {
        var sym = new GameSymbol("RunStateMachine", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LibUri, 0, null), null);
        var libDoc = new DocumentIndex(LibUri, 1, [sym], []);
        // myDoc has RequireArgs = ["PGStateMachine"] which resolves to LibUri
        var myDoc = new DocumentIndex(DocUri, 1, [], [],
            ImmutableArray.Create("PGStateMachine"));
        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(DocUri, myDoc)
                .Add(LibUri, libDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("RunStateMachine", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var entries = Collect("require(\"PGStateMachine\")", 0, 0,
            index, DocUri);
        Assert.Contains(entries, e => e.Name == "RunStateMachine" && e.Kind == ScopeEntryKind.RequiredGlobal);
    }

    // ── engine API ────────────────────────────────────────────────────────────

    [Fact]
    public void EngineApiFunction_IsInScope()
    {
        var schema = new FakeSchemaProvider("Find_Player");
        var entries = LuaLocalScopeCollector.CollectAt(
            "", 0, 0, DocUri, GameIndex.Empty, schema, FileHelper);
        Assert.Contains(entries, e => e.Name == "Find_Player" && e.Kind == ScopeEntryKind.EngineApi);
    }

    // ── Lua 5.1 builtins ─────────────────────────────────────────────────────

    [Fact]
    public void Lua51Builtins_AreInScope()
    {
        var entries = Collect("");
        Assert.Contains(entries, e => e.Name == "pairs" && e.Kind == ScopeEntryKind.Lua51Builtin);
        Assert.Contains(entries, e => e.Name == "table" && e.Kind == ScopeEntryKind.Lua51Builtin);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeSchemaProvider(params string[] names) : ILuaApiSchemaProvider
    {
        public IReadOnlySet<string> AllFunctionNames { get; } =
            new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName)
        {
            return [];
        }

        public string? GetFunctionDescription(string functionName)
        {
            return null;
        }

        public string? GetReturnTypeName(string functionName)
        {
            return null;
        }

        public IReadOnlyList<LuaTypeMember> GetMembersOf(string typeName)
        {
            return [];
        }
    }
}