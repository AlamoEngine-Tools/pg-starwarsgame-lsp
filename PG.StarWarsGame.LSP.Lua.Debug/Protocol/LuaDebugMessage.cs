// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Protocol;

/// <summary>One decoded or to-be-encoded inner message. Every concrete message is a record in this namespace.</summary>
public abstract record LuaDebugMessage
{
    public abstract LuaMessageId Id { get; }
}

/// <summary>A live script as the game lists it: its runtime id and the path it reports for it.</summary>
public sealed record ScriptEntry(int ScriptId, string FullPathName);

/// <summary>A named coroutine slot of a script. Unnamed slots are never sent.</summary>
public sealed record ThreadEntry(int ThreadIndex, string Name);

/// <summary>One member of a dumped table: key and value as the game renders them, with their Lua type codes.</summary>
public sealed record TableMember(int KeyType, string KeyText, int ValueType, string ValueText);
