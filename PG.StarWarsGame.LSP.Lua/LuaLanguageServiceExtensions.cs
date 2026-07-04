// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua;

public static class LuaLanguageServiceExtensions
{
    public static IServiceCollection AddLuaLanguageServices(this IServiceCollection services)
    {
        services.AddSingleton<LuaAnnotationRepository>();
        services.AddSingleton<ILuaAnnotationRepository>(sp => sp.GetRequiredService<LuaAnnotationRepository>());
        services.AddSingleton<LuaApiSchemaProxy>();
        services.AddSingleton<ILuaApiSchemaProvider>(sp => sp.GetRequiredService<LuaApiSchemaProxy>());
        // Shared parse source: one Loretta parse per (document, content) reused by indexing,
        // diagnostics (previously four parses per publish), and every request handler.
        services.AddSingleton<ILuaParseCache>(sp => new LuaParseCache(
            sp.GetRequiredService<IDocumentTextSource>(),
            sp.GetRequiredService<ServerOptions>().ParseCacheCapacity,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LuaParseCache>>()));
        services.AddSingleton<IGameDocumentParser, LuaGameDocumentParser>();
        services.AddSingleton<LuaDiagnosticsPublisher>();
        return services;
    }
}