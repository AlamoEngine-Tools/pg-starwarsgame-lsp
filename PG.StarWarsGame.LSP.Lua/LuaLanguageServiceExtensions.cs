// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
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
        services.AddSingleton<ICacheStatisticsSource>(sp => sp.GetRequiredService<LuaAnnotationRepository>());
        services.AddSingleton<LuaApiSchemaProxy>();
        services.AddSingleton<ILuaApiSchemaProvider>(sp => sp.GetRequiredService<LuaApiSchemaProxy>());
        // Shared parse source: one Loretta parse per (document, content) reused by indexing,
        // diagnostics (previously four parses per publish), and every request handler.
        services.AddSingleton<ILuaParseCache>(sp => new LuaParseCache(
            sp.GetRequiredService<IDocumentTextSource>(),
            sp.GetRequiredService<ServerOptions>().ParseCacheCapacity,
            sp.GetRequiredService<ILogger<LuaParseCache>>()));
        services.AddSingleton<ICacheStatisticsSource>(sp =>
            (ICacheStatisticsSource)sp.GetRequiredService<ILuaParseCache>());
        // One instance behind the parser contract and the statistics contract: the parser keeps
        // one serialized annotation state per file, and the status reports how much that holds.
        services.AddSingleton<LuaGameDocumentParser>();
        services.AddSingleton<IGameDocumentParser>(sp => sp.GetRequiredService<LuaGameDocumentParser>());
        services.AddSingleton<ICacheStatisticsSource>(sp => sp.GetRequiredService<LuaGameDocumentParser>());
        services.AddSingleton<LuaDiagnosticsPublisher>();
        services.AddSingleton<IDiagnosticsRepublisher>(sp => sp.GetRequiredService<LuaDiagnosticsPublisher>());
        return services;
    }
}