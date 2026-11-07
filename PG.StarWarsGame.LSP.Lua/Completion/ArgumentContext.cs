// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Completion;

/// <summary>The cursor is in an argument of a function call.</summary>
/// <param name="Quoted">
///     Inside the argument's string literal; otherwise before any quote, where a value is written with
///     its quotes.
/// </param>
internal sealed record ArgumentContext(string FunctionName, int ParamIndex, bool Quoted = true);