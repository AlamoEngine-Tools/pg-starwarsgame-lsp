// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Schema;

/// <summary>
///     Late-binding proxy for <see cref="ILuaApiSchemaProvider" />.
///     Starts empty; call <see cref="Configure" /> once the schema is loaded
///     (typically in the LSP server's OnInitialize callback).
/// </summary>
public sealed class LuaApiSchemaProxy : ILuaApiSchemaProvider
{
    private static readonly ILuaApiSchemaProvider Empty = new EmptyProvider();
    private volatile ILuaApiSchemaProvider _inner = Empty;

    public IReadOnlySet<string> AllFunctionNames => _inner.AllFunctionNames;

    public IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName)
    {
        return _inner.GetXmlRefs(functionName);
    }

    public string? GetFunctionDescription(string functionName)
    {
        return _inner.GetFunctionDescription(functionName);
    }

    public void Configure(ILuaApiSchemaProvider provider)
    {
        _inner = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    private sealed class EmptyProvider : ILuaApiSchemaProvider
    {
        public IReadOnlySet<string> AllFunctionNames => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName)
        {
            return [];
        }

        public string? GetFunctionDescription(string functionName)
        {
            return null;
        }
    }
}