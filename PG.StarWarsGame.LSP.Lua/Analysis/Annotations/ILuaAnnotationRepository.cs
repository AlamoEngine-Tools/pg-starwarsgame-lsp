// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

public interface ILuaAnnotationRepository
{
    void Update(string uri, ImmutableArray<EmmyLuaAnnotations> annotations);
    void Remove(string uri);
    IReadOnlyDictionary<string, ImmutableArray<EmmyLuaAnnotations>> All { get; }
    ILuaTypeIndex Current { get; }
    void RebuildIndex();
}
