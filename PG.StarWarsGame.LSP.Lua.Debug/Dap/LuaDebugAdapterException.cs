// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <summary>
///     A request the adapter cannot serve as asked; the message is what the user sees. It is the
///     protocol library's coded RPC error with an implementation-defined code, which is the one
///     shape that reaches the client with its message intact.
/// </summary>
public sealed class LuaDebugAdapterException : RpcErrorException
{
    /// <summary>Inside the range JSON-RPC reserves for implementation-defined server errors.</summary>
    public const int ErrorCode = -32001;

    public LuaDebugAdapterException(string message) : base(ErrorCode, message, message)
    {
    }

    public LuaDebugAdapterException(string message, Exception innerException) : base(ErrorCode, message, message)
    {
        Cause = innerException;
    }

    /// <summary>What went wrong underneath, kept for the log; the base type has no inner-exception slot.</summary>
    public Exception? Cause { get; }
}