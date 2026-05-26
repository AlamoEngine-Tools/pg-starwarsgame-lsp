// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaRequireResolverTest
{
    private static readonly IFileHelper s_fileHelper = new FileHelper(new MockFileSystem());

    private static readonly string[] WorkspaceUris =
    [
        "file:///data/scripts/library/pgstatemachine.lua",
        "file:///data/scripts/library/eawx-std/modcontentloader.lua",
        "file:///data/scripts/customfactionname.lua",
        "file:///data/scripts/miscellaneous/fleetevents.lua",
        "file:///data/scripts/library/spawn-sets/independent_forces.lua"
    ];

    [Fact]
    public void Resolve_BareNameInLibrary_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("PGStateMachine", WorkspaceUris, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_BareNameInOtherDirectory_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("FleetEvents", WorkspaceUris, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_SlashPath_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("eawx-std/ModContentLoader", WorkspaceUris, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_SlashPathSubdirectory_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("spawn-sets/INDEPENDENT_FORCES", WorkspaceUris, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_CaseInsensitive_ReturnsUri()
    {
        var result = LuaRequireResolver.Resolve("pgstatemachine", WorkspaceUris, s_fileHelper);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        var result = LuaRequireResolver.Resolve("NonExistentModule", WorkspaceUris, s_fileHelper);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_RelativePathWithDots_ReturnsSkip()
    {
        // Relative traversal (../../X) cannot be reliably resolved — should return null
        // without triggering a false-positive diagnostic (caller must distinguish).
        var result = LuaRequireResolver.Resolve("../../CustomFactionName", WorkspaceUris, s_fileHelper);
        Assert.Null(result);
    }

    [Fact]
    public void IsRelative_RelativeArg_ReturnsTrue()
    {
        Assert.True(LuaRequireResolver.IsRelative("../../CustomFactionName"));
        Assert.True(LuaRequireResolver.IsRelative("./LocalLib"));
        Assert.True(LuaRequireResolver.IsRelative("../Sibling"));
    }

    [Fact]
    public void IsRelative_AbsoluteOrBareArg_ReturnsFalse()
    {
        Assert.False(LuaRequireResolver.IsRelative("PGStateMachine"));
        Assert.False(LuaRequireResolver.IsRelative("eawx-std/ModContentLoader"));
    }

    [Fact]
    public void Resolve_BackslashPath_ReturnsUri()
    {
        // Engine scripts on Windows may use backslash separators in require args
        var fh = new FileHelper(new MockFileSystem());
        var result = LuaRequireResolver.Resolve("eawx-std\\ModContentLoader", WorkspaceUris, fh);
        Assert.NotNull(result);
    }
}