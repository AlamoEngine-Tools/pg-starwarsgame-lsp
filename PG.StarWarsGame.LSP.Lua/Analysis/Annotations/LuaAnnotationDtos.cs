// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

// Raw type reference - kept as the original string from the annotation.
public sealed record LuaTypeRef(string Raw)
{
    public static readonly LuaTypeRef Unknown = new("unknown");
    public bool IsEmpty => string.IsNullOrWhiteSpace(Raw);
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

public sealed record LuaFieldDefinition(
    string Name,
    bool IsOptional,
    LuaTypeRef Type,
    string? Description,
    LuaAccessModifier Access = LuaAccessModifier.Public);

public sealed record LuaClassDefinition(
    string Name,
    bool IsExact,
    ImmutableArray<string> Parents,
    ImmutableArray<LuaFieldDefinition> Fields,
    string? Description);

public sealed record LuaAliasDefinition(string Name, ImmutableArray<LuaTypeRef> Variants);

public sealed record LuaEnumDefinition(string Name, bool UseKeys);