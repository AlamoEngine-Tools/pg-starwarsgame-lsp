// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua;

public static class LuaDiagnosticCodes
{
    // ── own diagnostics ───────────────────────────────────────────────────────
    public const string RedundantRequire = "lua-redundant-require";
    public const string DuplicateRequire = "lua-duplicate-require";
    public const string EngineUpvalue = "lua-engine-upvalue";

    // ── Loretta diagnostic IDs (surfaced as LSP Code; no auto-fix available) ──
    // None of these are trivially auto-fixable: inserting `end` requires knowing
    // which block it closes; removing invalid chars changes user intent.
    public const string LorettaBadChar = "LUA0014";
    public const string LorettaExpressionAsStatement = "LUA0018";
    public const string LorettaUnsupportedOperator = "LUA0021";
    public const string LorettaIdentifierExpected = "LUA1001";
    public const string LorettaTokenExpected = "LUA1003";
    public const string LorettaKeywordExpected = "LUA1006";
    public const string LorettaInvalidExpression = "LUA1011";
    public const string LorettaInvalidStatement = "LUA1012";
}