// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>The stored CRC-32 of a datagram does not match its body.</summary>
public sealed class PgNetCrcException : PgNetProtocolException
{
    public PgNetCrcException(uint stored, uint actual)
        : base($"CRC mismatch: stored 0x{stored:x8}, actual 0x{actual:x8}")
    {
        Stored = stored;
        Actual = actual;
    }

    public uint Stored { get; }

    public uint Actual { get; }
}