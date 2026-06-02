// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal sealed record ScopeEntry(string Name, ScopeEntryKind Kind, string? Detail);

internal enum ScopeEntryKind
{
    LocalVariable,
    Parameter,
    OwnGlobal,
    RequiredGlobal,
    EngineApi,
    Lua51Builtin,
}
