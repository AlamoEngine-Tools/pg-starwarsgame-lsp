// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

public interface ILuaTypeIndex
{
    IReadOnlySet<string> AllTypeNames { get; }
    LuaClassDefinition? GetClass(string typeName);
    LuaAliasDefinition? GetAlias(string typeName);
    LuaEnumDefinition? GetEnum(string typeName);
}
