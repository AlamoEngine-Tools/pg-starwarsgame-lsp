// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Completion;

/// <summary>Cursor is on a bare identifier (not inside a string, not after a member access dot).</summary>
internal sealed record IdentifierContext(bool AtStatementStart) : LuaCompletionContext;
