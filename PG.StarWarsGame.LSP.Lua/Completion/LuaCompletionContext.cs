// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal abstract record LuaCompletionContext;

/// <summary>Cursor is inside a string literal that is an argument to a function call.</summary>
internal sealed record StringArgContext(string FunctionName, int ParamIndex) : LuaCompletionContext;

/// <summary>Cursor is right after a <c>.</c> (field access) or <c>:</c> (method call) operator.</summary>
internal sealed record MemberAccessContext(string? ReceiverName, bool IsMethodCall) : LuaCompletionContext;

/// <summary>Cursor is on a bare identifier (not inside a string, not after a member access dot).</summary>
internal sealed record IdentifierContext(bool AtStatementStart) : LuaCompletionContext;
