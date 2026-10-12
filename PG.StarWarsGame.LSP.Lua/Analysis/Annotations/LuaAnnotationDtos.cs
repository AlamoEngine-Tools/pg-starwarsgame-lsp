// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

// Raw type reference - kept as the original string from the annotation.
public sealed record LuaTypeRef(string Raw)
{
    public static readonly LuaTypeRef Unknown = new("unknown");
}

public sealed record LuaParamAnnotation(
    string Name,
    bool IsOptional,
    LuaTypeRef Type,
    string? Description);

public sealed record LuaReturnAnnotation(
    LuaTypeRef Type,
    string? Name,
    string? Description);