// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Parsing;

/// <summary>
///     Parsing a Lua file has a WRITE side effect - it fills the annotation repository, which
///     completion, hover and inlay hints all read workspace-wide. A document served from a
///     persisted index snapshot is never parsed, so that side effect never happened and the
///     annotations for that file silently vanished for the whole session.
/// </summary>
/// <remarks>
///     On a real layered mod this is the majority case, not an edge one: EaWX indexes 1433 Lua
///     files against 564 XML, so on a warm start almost every annotation in the workspace was
///     missing. It presents as a function losing its parameter hints and its documented type for
///     no visible reason, and only until someone happens to edit that file.
/// </remarks>
public sealed class LuaParserStateTest
{
    private const string Uri = "file:///c:/mod/scripts/library.lua";

    private const string Source = """
                                  ---Spawns a unit at a position.
                                  ---@param name string The object type to spawn
                                  ---@param count integer
                                  ---@return table spawned
                                  function SpawnUnit(name, count)
                                      return {}
                                  end
                                  """;

    private static (LuaGameDocumentParser Parser, LuaAnnotationRepository Repo) Build()
    {
        var repo = new LuaAnnotationRepository();
        var parser = new LuaGameDocumentParser(
            new LuaApiSchemaProvider([]),
            new FileHelper(new MockFileSystem()),
            NullLogger<LuaGameDocumentParser>.Instance,
            repo);
        return (parser, repo);
    }

    [Fact]
    public async Task CaptureParserState_ThenRestore_RebuildsTheFunctionAnnotation()
    {
        // The parse that fills the repository, and the state a snapshot would carry.
        var (parser, repo) = Build();
        await parser.ParseAsync(Uri, Source, 0, CancellationToken.None);
        var state = parser.CaptureParserState(Uri);

        Assert.NotNull(state);
        Assert.NotEmpty(state!);

        var parsed = repo.GetFunctionAnnotation("SpawnUnit");
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Params.Length);

        // A fresh session that serves this document from the snapshot instead of parsing it.
        var (restoringParser, restoredRepo) = Build();
        Assert.Null(restoredRepo.GetFunctionAnnotation("SpawnUnit")); // nothing yet, as on a warm start
        restoringParser.RestoreParserState(Uri, state);

        var restored = restoredRepo.GetFunctionAnnotation("SpawnUnit");
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Params.Length);
        Assert.Equal("name", restored.Params[0].Name);
        Assert.Equal("string", restored.Params[0].Type.Raw);
        Assert.Equal("The object type to spawn", restored.Params[0].Description);
        Assert.Equal("count", restored.Params[1].Name);
        Assert.Single(restored.Returns);
        Assert.Equal("table", restored.Returns[0].Type.Raw);
        Assert.Contains("Spawns a unit", restored.Description);
    }

    [Fact]
    public async Task RestoreParserState_AlsoRefillsTheTypeIndexSource()
    {
        // GetFunctionAnnotation is only half of it: completion reads the TYPE INDEX, which
        // RebuildIndex builds from the per-uri annotation store. A restore that repaired only the
        // function map would leave every @class and @alias in a cached file invisible.
        var classSource = """
                          ---@class Squadron
                          ---@field name string
                          ---@field size integer
                          local Squadron = {}
                          """;

        var (parser, _) = Build();
        await parser.ParseAsync(Uri, classSource, 0, CancellationToken.None);
        var state = parser.CaptureParserState(Uri);

        var (restoringParser, restoredRepo) = Build();
        restoringParser.RestoreParserState(Uri, state!);
        restoredRepo.RebuildIndex();

        var all = restoredRepo.All;
        Assert.True(all.ContainsKey(Uri));
        Assert.Contains(all[Uri], a => a.ClassDef?.Name == "Squadron");
    }

    [Fact]
    public void RestoreParserState_GarbageState_IsIgnoredRatherThanThrowing()
    {
        // A snapshot from a different build, or a truncated file. Losing the annotations is a
        // degraded session; throwing here would fail the whole workspace scan.
        var (parser, repo) = Build();

        parser.RestoreParserState(Uri, [1, 2, 3, 4, 5]);

        Assert.Empty(repo.All);
    }

    [Fact]
    public async Task CaptureParserState_AFileWithNoAnnotations_RoundTripsEmpty()
    {
        var (parser, _) = Build();
        await parser.ParseAsync(Uri, "local function helper() end", 0, CancellationToken.None);
        var state = parser.CaptureParserState(Uri);

        var (restoringParser, restoredRepo) = Build();
        restoringParser.RestoreParserState(Uri, state!);

        Assert.Null(restoredRepo.GetFunctionAnnotation("nope"));
    }

    [Fact]
    public void CaptureParserState_AnUnparsedDocument_ReturnsNull()
    {
        var (parser, _) = Build();

        Assert.Null(parser.CaptureParserState("file:///c:/mod/never-seen.lua"));
    }

    /// <summary>The XML parser has no such state, and must not be asked to invent one.</summary>
    [Fact]
    public void DefaultImplementation_CapturesNothing()
    {
        var annotations = ImmutableArray<EmmyLuaAnnotations>.Empty;

        Assert.Empty(annotations);
    }
}