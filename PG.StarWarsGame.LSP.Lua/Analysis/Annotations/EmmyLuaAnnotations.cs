// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

/// <summary>
///     What this server reads from an EmmyLua doc-comment block: the prose, and the parameters and
///     returns in declaration order. Everything else the block declares is the analyzer's (#155).
/// </summary>
public sealed record EmmyLuaAnnotations(
    /// <summary>Accumulated prose lines (non-@ comment text), joined with \n.</summary>
    string? Description,
    ImmutableArray<LuaParamAnnotation> Params,
    ImmutableArray<LuaReturnAnnotation> Returns)
{
    public static readonly EmmyLuaAnnotations Empty = new(
        null,
        ImmutableArray<LuaParamAnnotation>.Empty,
        ImmutableArray<LuaReturnAnnotation>.Empty);

    public bool HasContent =>
        Description is not null ||
        !Params.IsDefaultOrEmpty ||
        !Returns.IsDefaultOrEmpty;
}