// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests;

/// <summary>A sandbox provider that always answers the one sandbox it was given.</summary>
internal sealed class FixedSandbox(LuaSandbox sandbox) : ILuaSandboxProvider
{
    public LuaSandbox Current => sandbox;

    /// <summary>
    ///     A sandbox that knows no script: every pair of files shares a state and every rename stays
    ///     unscoped, exactly what a workspace with no Lua documents gets.
    /// </summary>
    public static FixedSandbox Empty()
    {
        return new FixedSandbox(LuaSandbox.Build(
            ImmutableDictionary<string, DocumentIndex>.Empty, new FileHelper(new MockFileSystem())));
    }
}