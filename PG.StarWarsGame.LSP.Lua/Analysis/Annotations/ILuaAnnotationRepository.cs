// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

public interface ILuaAnnotationRepository
{
    IReadOnlyDictionary<string, ImmutableArray<EmmyLuaAnnotations>> All { get; }
    ILuaTypeIndex Current { get; }
    void Update(string uri, ImmutableArray<EmmyLuaAnnotations> annotations);
    void Remove(string uri);
    void RebuildIndex();

    void UpdateFunctionAnnotations(string uri, IReadOnlyList<(string Name, EmmyLuaAnnotations Ann)> functions);
    EmmyLuaAnnotations? GetFunctionAnnotation(string name);
}