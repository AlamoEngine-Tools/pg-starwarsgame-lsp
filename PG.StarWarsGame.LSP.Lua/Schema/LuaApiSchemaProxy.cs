// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Lua.Schema;

/// <summary>
///     Late-binding proxy for <see cref="ILuaApiSchemaProvider" />.
///     Starts empty; call <see cref="LateBindingProxy{T}.Configure" /> once the schema is loaded
///     (typically in the LSP server's OnInitialize callback).
/// </summary>
public sealed class LuaApiSchemaProxy : LateBindingProxy<ILuaApiSchemaProvider>, ILuaApiSchemaProvider
{
    public LuaApiSchemaProxy() : base(new EmptyProvider()) { }

    public IReadOnlySet<string> AllFunctionNames => Inner.AllFunctionNames;

    public IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName) =>
        Inner.GetXmlRefs(functionName);

    public string? GetFunctionDescription(string functionName) =>
        Inner.GetFunctionDescription(functionName);

    public string? GetReturnTypeName(string functionName) =>
        Inner.GetReturnTypeName(functionName);

    public IReadOnlyList<LuaTypeMember> GetMembersOf(string typeName) =>
        Inner.GetMembersOf(typeName);

    public LuaClassDefinition? GetClassDefinition(string typeName) =>
        Inner.GetClassDefinition(typeName);

    private sealed class EmptyProvider : ILuaApiSchemaProvider
    {
        public IReadOnlySet<string> AllFunctionNames =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName) => [];
        public string? GetFunctionDescription(string functionName) => null;
        public string? GetReturnTypeName(string functionName) => null;
        public IReadOnlyList<LuaTypeMember> GetMembersOf(string typeName) => [];
        public LuaClassDefinition? GetClassDefinition(string typeName) => null;
    }
}
