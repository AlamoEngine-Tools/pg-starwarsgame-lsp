// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests;

/// <summary>A sandbox provider that always answers the one sandbox it was given.</summary>
internal sealed class FixedSandbox(LuaSandbox sandbox) : ILuaSandboxProvider
{
    public LuaSandbox Current => sandbox;
}