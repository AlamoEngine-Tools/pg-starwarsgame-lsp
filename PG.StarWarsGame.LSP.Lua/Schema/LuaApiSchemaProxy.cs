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
    public LuaApiSchemaProxy() : base(new EmptyProvider())
    {
    }

    public IReadOnlySet<string> AllFunctionNames => Inner.AllFunctionNames;

    public IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName)
    {
        return Inner.GetXmlRefs(functionName);
    }

    public string? GetFunctionDescription(string functionName)
    {
        return Inner.GetFunctionDescription(functionName);
    }

    public string? GetReturnTypeName(string functionName)
    {
        return Inner.GetReturnTypeName(functionName);
    }

    public IReadOnlyList<LuaTypeMember> GetMembersOf(string typeName)
    {
        return Inner.GetMembersOf(typeName);
    }

    public LuaClassDefinition? GetClassDefinition(string typeName)
    {
        return Inner.GetClassDefinition(typeName);
    }

    public IReadOnlyList<LuaParamAnnotation> GetFunctionParams(string functionName)
    {
        return Inner.GetFunctionParams(functionName);
    }

    private sealed class EmptyProvider : ILuaApiSchemaProvider
    {
        public IReadOnlySet<string> AllFunctionNames =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName)
        {
            return [];
        }

        public string? GetFunctionDescription(string functionName)
        {
            return null;
        }

        public string? GetReturnTypeName(string functionName)
        {
            return null;
        }

        public IReadOnlyList<LuaTypeMember> GetMembersOf(string typeName)
        {
            return [];
        }

        public LuaClassDefinition? GetClassDefinition(string typeName)
        {
            return null;
        }

        public IReadOnlyList<LuaParamAnnotation> GetFunctionParams(string functionName)
        {
            return [];
        }
    }
}