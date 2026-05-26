// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Lua.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua;

public static class LuaLanguageServiceExtensions
{
    public static IServiceCollection AddLuaLanguageServices(this IServiceCollection services)
    {
        services.AddSingleton<LuaApiSchemaProxy>();
        services.AddSingleton<ILuaApiSchemaProvider>(sp => sp.GetRequiredService<LuaApiSchemaProxy>());
        services.AddSingleton<IGameDocumentParser, LuaGameDocumentParser>();
        services.AddSingleton<LuaDiagnosticsPublisher>();
        return services;
    }
}