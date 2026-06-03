// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Completion;

/// <summary>Cursor is right after a <c>.</c> (field access) or <c>:</c> (method call) operator.</summary>
internal sealed record MemberAccessContext(string? ReceiverName, bool IsMethodCall) : LuaCompletionContext;