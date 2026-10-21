// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Schema;

/// <summary>
///     One stub file as the schema's manifest lists it: its file name and its text.
/// </summary>
/// <remarks>
///     The name is what tells the engine's own files (<c>api.d.lua</c>, <c>stdlib.d.lua</c>,
///     <c>globals.d.lua</c>) from a vanilla library's documentation (<c>pgbase.d.lua</c> and the
///     rest): the former declare what every script can call, the latter describe what a script
///     gets by requiring that library.
/// </remarks>
/// <param name="Name">The file name, as listed in <c>lua/_files.json</c>.</param>
/// <param name="Content">The file's text.</param>
public sealed record LuaStubFile(string Name, string Content);

/// <summary>What a stub says about a global value: its type, and its description when it has one.</summary>
public sealed record LuaGlobalAnnotation(string TypeName, string? Description);
