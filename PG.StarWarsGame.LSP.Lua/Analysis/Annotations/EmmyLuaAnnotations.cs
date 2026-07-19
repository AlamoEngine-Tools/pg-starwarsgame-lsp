// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

/// <summary>
///     Aggregate output of parsing a single EmmyLua doc-comment block.
/// </summary>
public sealed record EmmyLuaAnnotations(
    /// <summary>Accumulated prose lines (non-@ comment text), joined with \n.</summary>
    string? Description,
    /// <summary>@class declaration, if present.</summary>
    LuaClassDefinition? ClassDef,
    /// <summary>@alias declaration, if present.</summary>
    LuaAliasDefinition? AliasDef,
    /// <summary>@enum declaration, if present.</summary>
    LuaEnumDefinition? EnumDef,
    /// <summary>@type annotation for the following variable.</summary>
    LuaTypeRef? TypeAnnotation,
    ImmutableArray<LuaParamAnnotation> Params,
    ImmutableArray<LuaReturnAnnotation> Returns,
    /// <summary>@overload raw strings (e.g. "fun(a: string): void").</summary>
    ImmutableArray<string> Overloads,
    /// <summary>@generic type parameter names (e.g. ["T", "U"]).</summary>
    ImmutableArray<string> GenericParams,
    bool IsDeprecated,
    bool IsNodiscard,
    bool IsAsync,
    /// <summary>@private / @protected / @package access modifier, if any.</summary>
    LuaAccessModifier? AccessModifier,
    ImmutableArray<string> SeeRefs)
{
    public static readonly EmmyLuaAnnotations Empty = new(
        null, null, null, null, null,
        ImmutableArray<LuaParamAnnotation>.Empty,
        ImmutableArray<LuaReturnAnnotation>.Empty,
        ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty,
        false, false, false, null,
        ImmutableArray<string>.Empty);

    public bool HasContent =>
        Description is not null ||
        ClassDef is not null ||
        AliasDef is not null ||
        EnumDef is not null ||
        TypeAnnotation is not null ||
        !Params.IsDefaultOrEmpty ||
        !Returns.IsDefaultOrEmpty ||
        IsDeprecated || IsNodiscard || IsAsync;
}