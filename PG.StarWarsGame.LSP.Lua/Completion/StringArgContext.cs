// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Completion;

/// <summary>Cursor is inside a string literal that is an argument to a function call.</summary>
internal sealed record StringArgContext(string FunctionName, int ParamIndex) : LuaCompletionContext;